### Overview
A research codebase with an ad hoc collection of PowerShell cmdlets for working with remotely sensed data, primarily point clouds.

- `Get-Dsm`: get digital surface, canopy maxima, and canopy height models from a set of point cloud tiles with supporting information
- `Get-GridMetrics`: get z, intensity, and other common grid metrics from a set of point cloud tiles
- `Get-Orthoimages`: get 16 bit RGB+NIR orthoimages with LiDAR intensity bands from point clouds
- `Get-TreeTops`: find treetop candidates in a digital surface or canopy height model

The cmdlets are multithreaded at the tile level and attempt to self configure to reasonable defaults. The current configuration logic is 
nascent and manually setting `-ReadThreads` and `-MaxThreads` on cmdlets which offer them may improve performance or be needed to constrain
memory use. [LAS](https://www.asprs.org/divisions-committees/lidar-division/laser-las-file-format-exchange-activities) and [GDAL](https://gdal.org/) 
file formats are supported, though GDAL testing is limited to GeoPackage and GeoTIFF. In cases where directories are searched for data tiles 
.las and .tif are the default file extensions.

DRAM utilization varies with dataset structure and with parallelism. AMD Zen 3, Intel Raptor Lake, or newer processors with 12–16 cores and 
64–128 GB of DDR are assumed as typical hardware. Development and testing extend to point cloud tile sets up to 2 TB with most such processing 
tasks fitting within 64 GB DDR, though 96 or 128 GB is preferable. Tile processing rates depend on drive and processor core capabilities but 
.las read speeds up to 3.7 GB/s have been shown to be sustainable and peak transfer rates of 7 GB/s have been observed from some cmdlets. AVX, 
AVX2, and FMA instructions are used at times. AVX10/256 and AVX10/512 are not currently utilized.

QGIS (as of the 3.34 LTR) tends to be slow to work with virtual rasters produced from LiDAR point cloud processing, apparently due to GDAL's 
inclination to set `BlockYSize="1"` in .vrt files in contradiction to the guidance in GDAL's own documentation. Clouds can't really do anything 
about this, though it adheres to GDAL guidance and offers a cmdlet to remove block sizes.

Code is currently pre-alpha and provided as is. Typically, the head commit should compile and pass unit tests but this isn't guaranteed. 
APIs are volatile and breaking changes are likely.

### Supporting cmdlets
Supporting tools are

- `Get-LocalMaxima`: get local maxima radii in a DSM (or DTM)
- `Get-ScanMetrics`: similar to `Get-GridMetrics` but reports on data acquisition (scan angle and direction, noise and withheld points, flags)
- `Get-Vrt`: a [gdalbuildvrt](https://gdal.org/programs/gdalbuildvrt.html) alternative supporting sharded tile sets with fixes for other limitations
- `Get-LasInfo`: read a .las or .laz file's header
- `Register-Cloud`: set .las files' origin, coordinate system, and source ID
- `Convert-CloudCrs`: reproject .las files to a new coordinate system, adding a vertical coordinate system if one is not present
- `Remove-Points`: remove high noise, low noise, and withheld points from a point cloud
- `Repair-NoisePoints`: mark z outliers as high and low noise relative to DTM (useful for that one point in low Earth orbit)
- `Export-VrtBands`: work around QGIS performance limitations in displaying virtual rasters by extracting subsets of the bands in a .vrt
- `Remove-VrtBlockSize`: work around QGIS's tendency to further degrade its performance displaying virtual rasters (see below)
- `Get-TreeSize`: get sizes of directories on disk, including some common file types (filesystem trees, not actual trees)
- `Convert-DiskSpd`: reformat [DiskSpd](https://github.com/microsoft/diskspd) .xml result files as longform data

### Dataset structure
A LiDAR dataset is assumed to consist of

- .las point cloud tiles
- .tif digital terrain models
- .tif raster tiles generated from the point cloud tiles at fairly high resolution (typically 10 cm to 1 m, `Get-Dsm`, `Get-Orthoimages`)
- .tif single rasters covering the study area at lower resolution (typically 10–30 m, `Get-GridMetrics`, `Get-ScanMetrics`)
- .gpkg vector tiles of treetops located in a digital surface model (`Get-TreeTops`)

It's assumed all of the tiles use the same coordinate reference system, are aligned with the coordinate system, are the same size, and
provided gapless, non-overlapping coverage of the study area. Grid metrics, scan metrics, orthoimagery, and the digital terrain model (DTM)
can all have different cell sizes. Cells in grid and scan metrics rasters can span multiple tiles. For orthoimagery and DTMs, cells are 
aligned with tiles and tile sizes are taken to be exact multiples of the cell size. Digital surface models (DSMs) and their associated 
layers are generated at the same resolution and alignment as the DTM. These constraints can be relaxed but, due the the additional code
complexity and computation involved, support for more flexibile alignment hasn't been an implementation priority.

Outputs often consist of data tiles with accompanying diagnostic tiles. This arrangement is used to keep file sizes more manageable and
avoid collapses observed with tools such as QGIS which, as version 3.28.15 (January 2024), become effectively unusuable on data such as 
100 GB virtual rasters with a dozen bands. Subsequent processing, such as treetop identification, may load bands from both data and
diagnostic raster tiles.

### No data values
No data values are detected and ignored for all rasters and, in general, propagate. For example, a no data value in either the digital 
surface or digital terrain model results in a no data value for the canopy height model. Generation of pit free surface models or other
types of no data filling is therefore not required, though alternate approaches fitting canopy models ([Paris and Bruzzone 2015](https://doi.org/10.1109/TGRS.2014.2324016)) 
will likely be beneficial on sparse point clouds. NaN (not a number) is used as the default no data value for floating point bands. For
signed integer bands the most negative value (-128, -32,768, -2,147,483,648, or -9,223,372,036,854,775,808) is the default no data and,
for unsigned integers, the most positive value is used (255, 65,535, 4,294,967,295, or 18,446,744,073,709,551,615).

No data values can differ among tiles of virtual rasters, often necessarily so when tiles are sharded into files of different data types 
or when tile data types are heterogenous. If raster bands are converted to wider types (for example, bytes to 16 bit unsigned integers) 
no data values are left unchanged but, on conversion to narrower types, no data values are propagated. When it is unavoidable, data is 
altered slightly to allow propagation, most commonly by changing 65535 in image data to 65534.

### Maximizing LiDAR processing throughput
In general, Clouds is IO bound when .las tiles are being read from hard drives or from flash drives with moderate bandwidth connections
such as USB 3 gen 1 (5 Gb/s) or SATA III (6 Gb/s). Unless a cmdlet performs minimal computation, then Clouds is likely to be compute bound 
when .las files are read from NVMe drives or when working with raster data. The exact balance between IO and compute throughput depends on 
drive and CPU core capabilities, Clouds' algorithmic efficiency and code optimization status, the amount of DDR available relative to the needs
of the specific data processing being done, the state of the data structures used for processing, and the extent to which Clouds can circumvent
performance bottlenecks in GDAL. Since the overhead of idle worker threads is small, performance disavantages tend to be negligible when compute 
throughput exceeds IO. Memory pressure tends to occur when IO exceeds compute throughput, both because IO loads point clouds into memory until 
the tile limit (`-MaxPointTiles`) is reached and because each each additional compute thread allocates additional working data structures.

Currently, Clouds is limited to processing tiles in the same rowwise order as GDAL uses for rasters, meaning grids of tiles in typical projected 
coordinate systems (+x is east and +y is north) are traversed rowwise from north to south and from west to east in each row. When tiles can be
processed independently of each other this loading order usually has little effect on IO and memory requirements are primarily a function of the
number of compute threads (Clouds commonly defaults to one thread per core, though some cmdlets benefit substantially from hyperthreading and
therefore default to more than one thread per core—see each cmdlet's help for details). When processing needs to access data in neighboring
tiles then each previous east-west row of tiles is retained until the next row is completed. In such cases the width of the area of interest can
become significant to total memory requirements. As Clouds does not currently support east to west, rather than north to south traversal, memory
requirements can be unnecessarily elevated for study areas whose east-west extent is larger than their north-south extent (changing traversal 
direction based on tile grid extent is not especially difficult but this feature hasn't gotten implementation priority as yet).

Clouds' requirements on the amount of DDR physically installed in a system therefore scale with the size of the study area, density of the point
clouds, resolution chosen for rasters, and the number of cores utilized. Clouds assumes DDR was purchased with the intent of using it to make
data processing efficient and, as a rule of thumb, 64 GB per eight processor cores and 64 GB per terabyte of point cloud data is most likely 
sufficient to avoid memory constraints on performance. If point cloud resolution resolution is high enough and the study area large enough then 
Clouds' memory requirements will exceed the amount of DDR available on desktop platforms (128 GB for DDR4 and, at time of writing, 192 GB for 
DDR5 UDIMMs). DDR exhaustion has not been much of an issue on the projects Clouds is being developed with but memory management will receive
additional attention as need arises.

Perhaps the main consideration in LiDAR processing throughput is the ratio between IO and compute rates. For example, if a core can read or
write data at 2 GB/s or, if not doing IO, can do the needed processing computations on 300 MB of data per second, then an eight core processor
achieves a good IO to compute throughput balance with one core doing IO and the other seven cores doing compute. Similarly, a 16 core CPU
would tend to have two cores doing IO and 14 doing compute. It's usually undesirable to affinitize any particular core or thread to a particular
task. Even when all cores have the same design (AMD Zen 3 and 4 desktop parts, for example), individual cores will boost to different frequencies
depending on the core performance level, total socket power demand, and thermals. Processing rates therefore vary on a per core basis and,
additionally, a core's throughput is likely to vary somewhat over time as its boost level shifts and as it experiences different access speeds 
to L3 cache, DDR main memory, and cross-thread synchronization mechanisms such as locks. Thread throughput varies somewhat as well, as the 
operating system's scheduler may decide to move threads between cores. Processing priorities likely also shift. For example, when processing 
begins no point clouds have been read and, therefore, cores have nothing to do but IO. Conversely, if a row of tiles has been fully read and 
all of the row's intermediate processing is done, then it's desirable to complete the row's processing and write tiles' outputs to disk to 
release memory for processing subsequent rows. In the case of a 12 core CPU in the example above, it's likely that at any given time one thread 
would running IO, 10 threads would be doing computations, and the twelfth slot would switch back and forth between IO and computation depending
on whether it was higher priority to load more tiles or to complete processing of already loaded tiles. However, if the data source is a single
hard drive, then limiting IO to a single thread is desirable. If multiple threads attempt to perform concurrent IO it's very likely they'll 
contend for the actuator, decreasing overall performance. AMD processors with both standard and dense cores, multi-CCD processors with 3D vcache
on one CCD, and Intel processors with P- and E-cores are likely to exhibit greater asymmetries in processing rates, both among cores and when
the operating system chooses to move threads between cores.

Clouds cmdlets therefore favor fully asynchronous parallelism where, when a thread completes a processing step, it considers current workload 
status and determines which step would by most useful to start on next. This design attempts to maximize throughput by fully utilizing all
available resources. If more than one cmdlet, or if other workloads need to run concurrently, utilization can be restricted via `-MaxThreads`
and other throttling parameters.

At present, Clouds does not try to schedule reads optimally across drives. The current assumption is all tiles to be read are either 1) within a
single volume, either a single disk or one RAID, or 2) if tiles are located on multiple volumes that the tiles are striped across volumes in
the same north to south, west to east spatial order that the tile grid is traversed in. In the latter case, Clouds assumes the volumes' read 
speeds are well matched to the striping layout and thus that tile loading remains well balanced within the number of tiles allowed to be
simultaneously loaded in memory. In general, this means multi-volume data layouts should likely span a set of JBOD hard drives with similar
performance characteristics (including volumes matching single actuators in multi-actuator drives) or similar JBOD NVMes.

A few other performance details are notable.

- Synchronous IO in Windows often reaches maximum speeds of 1–2 GB/s per thread in practical workloads. While drive benchmarking tools such as 
  [DiskSpd](https://github.com/microsoft/diskspd) (of which [CrystalDiskMark](https://crystalmark.info/en/software/crystaldiskmark/) uses an 
  older version internally) and .NET IO benchmarks reach transfer rates of 7 and 14 GB/s for PCIe 4.0 x4 and 5.0 x4 NVMe drives, benchmarks
  need only move data into or out of memory. In point cloud processing workloads, data is read off drives to be used and data being written 
  has to be generated, both of which require computations and additional DDR transfers not present in benchmarking. Profiling of Clouds shows 
  ±20% or more variations in throughput depending on selection among cmdlet implementation tradeoffs and the number of threads running. Absent
  substantial RAM overclocking, desktop processors' dual channel bandwidth to DDR4 is increasingly likely to become a limiting factor near the 
  3.5 GB/s throughput limit of a PCIe 3.0 x4 drive. With DDR5, throughput upper bounds of of 4–5 GB/s are likely. Monitoring tools, such as
  [HWiNFO64](https://www.hwinfo.com/), can be helpful in tracking DDR and drive transfer rates.
- Read speeds above roughly 1 GB/s tend to be stressful to .NET memory management. Best practices are used for object pooling and large object
  heap offloading but it can take longer for the garbage collector to work through tens of gigabytes of first generation, second generation, and
  large objects than it does to run a cmdlet. Cmdlets may therefore request a second generation collection and compaction of the managed heap
  just before they exit. Windows reports process memory consumption based on heap extent so, prior to compaction, Clouds cmdlets may appear to
  be using substantially more memory than they actually are.
- Windows does not appear to have a documented, programmatic, non-administrative mechanism for determining the RAID level of arrays created with
  Disk Management (dynamic disks). As a result, Clouds assumes dynamic disks are RAID0 (striped) volumes with read speeds slow enough a single 
  read thread can fully utilize the array. For RAID1 (mirrored volumes) or RAID5 (parity), setting `-ReadThreads` to the number of data copies 
  present will likely result in full utilization of hard drive based RAIDs. Use of `-ReadThreads` is not necessary with Storage Spaces as Windows 
  reports a virtual disk's number of data copies.
- Windows also lacks a way of programatically querying a hard drives' number of actuators. As RAID is usually configured to use multi-actuator 
  drives and multi-actuator drives are uncommon Clouds does not maintain a list of multi-actuator drive models or attempt to map the disk volume 
  arrangements being used to actuators. If Exos 2X18 halves or similar hardware pairings are being used in JBOD with tiles striped across the 
  halves then `-ReadThreads 2` allows Clouds to operate both actuators simultaneously.

### Dependencies
Clouds is a .NET 8.0 assembly which includes C# cmdlets for PowerShell Core. It therefore makes use of both the System.Management.Automation
nuget package and the system's PowerShell Core installation, creating a requirement the PowerShell Core version be the same or newer than 
the nuget's. If Visual Studio Code is used for PowerShell Core execution then corresponding updates to Visual Studio Code and its PowerShell 
extension are required.

Clouds relies on GDAL for GIS operations. This imposes performance bottlenecks in certain situations, mainly where GDAL forces single threaded 
write transactions on large GeoPackages.

Clouds is developed using current or near-current versions of [Visual Studio Community](https://visualstudio.microsoft.com/downloads/) 
edition. Clouds is only tested on Windows 10 22H2 but should run on any .NET supported platform.

### Limitations
Currently, each rasterization cmdlet performs its own independent read of point cloud tiles. With 3.5 inch drives this likely means tile
processing bottlenecks on the drive. Scripting cmdlets sequences and letting them running unattended mitigates read speed limitations. 
However, the redundant reads still accumulate towards drives' annual workload rate limits.

GeoTIFFs with different datatypes in different bands are perhaps best described as semi-supported within the [OSGEO](https://www.osgeo.org/) 
toolset. Currently GeoTIFFs are written with all bands being of the same type using GDAL's default no data profile that requires all bands
have the same no data value. Investigation is needed to assess use of other GeoTIFF profiles, heterogenous band types within GeoTIFFs, and
raster formats with more complete support for mixed band types.

Also,

- Currently NAVD88 is the only well known vertical coordinate system. `Convert-CloudCrs`'s imputation of missing vertical coordinate systems
  therefore doesn't behave correctly outside of North America.
- Supported raster data types are real values (8, 16, 32, and 64 bit signed and unsigned integers, single and double precision floating point).
  While GDAL also supports complex values and two other cell types, Clouds does not.
- QGIS will modify virtual rasters (.vrts) by adding or updatating band metadata and approximate histograms when a project is closed, often 
  following changes to symbology. When QGIS does this it inserts `BlockYSize="1"` attributes on all the tiles listed in the .vrt or replaces
  existing `BlockYSize` values with 1, dramatically reducing performance for virtual rasters composed of GeoTIFF tiles once the QGIS project 
  is reopened. `Remove-VrtBlockSize` can be used to strip out the reintroduced `BlockXSize` and `BlockYSize` attributes (find/replace on the 
  .vrt in a text editor does the same thing).
- In certain situations GDAL bypasses its callers and writes directly to the console. The common case for this is `ERROR 4` when a malformed 
  dataset (GIS file) is encountered. If error 4 occurs during a raster write Clouds catches it and tries to recreate the raster, which typically 
  succeeds,but Clouds has no ability to prevent the error from appearing in PowerShell.
- Interactions with parity RAID arrays haven't been tested, so default behavior with RAID5 or 6 may be poor. `-ReadThreads` should provide a
  workaround, if needed.
