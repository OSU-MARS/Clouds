### Overview
A forest biometrics research codebase with an ad hoc collection of PowerShell cmdlets for working with remotely sensed data, primarily point 
clouds.

- `Get-Dsm`: get digital surface, canopy maxima, and canopy height models from a set of point cloud tiles with supporting information
- `Get-Orthoimages`: get 16 bit RGB+NIR orthoimages with LiDAR intensity bands from point clouds
- `Get-Treetops`: simple unsupervised classification of local maxima as treetop candidates in a digital surface or canopy height model
- `Get-Crowns`: treetop seeded segmentation of individual tree crowns from a digital surface model using path cost functions
- `Get-GridMetrics`: get z, intensity, and other common grid metrics from a set of point cloud tiles
- `Register-Clouds`: set .las files' origin, coordinate system, source ID, and repair LAS compliance issues left by SLAM tools
- `Export-Slices`: extract mean intensity rasters for stem mapping using points between a minimum and maximum height

The cmdlets are multithreaded at the tile level and attempt to self configure to reasonable defaults. The current configuration logic is 
nascent and manually setting `-ReadThreads` and `-DataThreads` on cmdlets which offer them may improve performance or be needed to constrain
memory use. [LAS](https://www.asprs.org/divisions-committees/lidar-division/laser-las-file-format-exchange-activities) and [GDAL](https://gdal.org/) 
file formats are supported, though GDAL testing is limited to GeoPackage and GeoTIFF. In cases where directories are searched for data tiles 
.las and .tif are the default file extensions.

Monitoring of component temperatures is advised during workload characterization. System fan speeds are most commonly controlled by processor
temperatures but often are not linked to DDR or drive temperatures. In IO intensive workloads with little compute demand the processor may remain
cool while drives get hot or, if poorly positioned for airflow, drives may just become hot during the long running, intensive operations 
characteristic of LiDAR processing. For example, larger datasets can overwhelm motherboard armor's heat dissipation ability and pushing even energy 
efficient PCIe 4.0 x4 NVMe drives with good thermal contact out of their operating temperature range. Thermal throttling can result and, in
drives lacking throttling, drives may be permanently marked with S.M.A.R.T. errors. If in doubt, stress test drives and upgrade cooling as
needed. On Windows systems which are not OEM locked, [FanControl](https://github.com/Rem0o/FanControl.Releases) may be helpful for linking
airflow to drive temperatures.

Clouds' DRAM utilization varies with dataset structure and with parallelism. AMD Zen 3, Intel Raptor Lake, or newer processors with 12–16 cores 
and 64–128 GB of DDR (6-8 GB DDR per core) are assumed as typical hardware. Development and testing extend to point cloud tile sets up to 2 TB
with most processing likely fitting within 64 GB of DDR per terabyte of point cloud data. Tile processing rates depend on drive and processor 
core capabilities but desktop hardware has been shown to sustain .las read speeds up to 4.9 GB/s with operating system agnostic code, increasing
to 7.0 GB/s with operating system specific optimizations. Also, peak transfer rates of 7 GB/s have been observed from some cmdlets. AVX, AVX2,
and FMA instructions are used at times for processing. AVX10/256 and AVX10/512 are not currently utilized.

Code is currently pre-alpha and provided as is. Typically, the head commit should compile and pass unit tests but this isn't guaranteed (the
commit before head should be fine). APIs are volatile and breaking changes are routine.

### Supporting cmdlets
Besides the main PowerShell cmdlets listed above, Clouds includes several supporting cmdlets for summarizing other information from point clouds,
manipulating point clouds, working with virtual rasters, and characterizing drive utilization.

- `Get-LocalMaxima`: get local maxima radii in a DSM (or DTM)
- `Get-ScanMetrics`: similar to `Get-GridMetrics` but reports on data acquisition (scan angle and direction, noise and withheld points, flags)
- `Get-Vrt`: a [gdalbuildvrt](https://gdal.org/programs/gdalbuildvrt.html) alternative supporting sharded tile sets with fixes for other limitations
- `Get-LasInfo`: read a .las or .laz file's header
- `Get-DsmSlopeAspect`: get slope and aspect of a digital surface, canopy maxima, canopy height model
- `Convert-CloudCrs`: reproject .las files to a new coordinate system, adding a vertical coordinate system if one is not present
- `Get-BoundingBoxes`: get a set of .las files' bounding boxes as a polygon layer, useful for indexing tiles
- `Remove-Points`: remove high noise, low noise, and withheld points from a point cloud
- `Repair-NoisePoints`: mark z outliers as high and low noise relative to DTM (useful for that one point in low Earth orbit)
- `Export-VrtBands`: work around QGIS performance limitations in displaying virtual rasters by extracting subsets of the bands in a .vrt
- `Remove-VrtBlockSize`: work around QGIS's tendency to further degrade its performance displaying virtual rasters (see below)
- `Get-TreeSize`: get sizes of directories on disk, including some common file types (filesystem trees, not actual trees)
- `Convert-DiskSpd`: reformat [DiskSpd](https://github.com/microsoft/diskspd) .xml result files as longform data
- `Read-Files`: complements DiskSpd with multithreaded reads of directories or wildcarded files for drive load testing
- `Get-SortPerformance`: profiles introspective and radix sort times on point cloud grid metrics cells

### LiDAR dataset (project) file structure
A LiDAR dataset is assumed to consist of

- .las point cloud tiles
- .tif digital terrain models
- .tif raster tiles generated from the point cloud tiles at fairly high resolution (typically 10 cm to 1 m, `Get-Dsm`, `Get-Orthoimages`)
- .tif single rasters covering the study area at lower resolution (typically 10–30 m, `Get-GridMetrics`, `Get-ScanMetrics`)
- .gpkg ([GeoPackage](https://www.geopackage.org/)) vector tiles of treetops located in a digital surface model (`Get-TreeTops`)

It's assumed all of the tiles use the same coordinate reference system, are aligned with the coordinate system, are the same size, and
provide gapless, non-overlapping coverage of the study area. Grid metrics, scan metrics, orthoimagery, and the digital terrain model (DTM)
can all have different cell sizes. Cells in grid and scan metrics rasters can span multiple tiles. For orthoimagery and DTMs, cells must 
be aligned with tiles and tile sizes are taken to be exact multiples of the cell size. Digital surface models (DSMs) and their associated 
layers are generated at the same resolution and alignment as the DTM. These constraints can be relaxed but, due the the additional code
complexity and computation involved, support for more flexibile alignment hasn't been an implementation priority.

Outputs often consist of data tiles with accompanying diagnostic tiles. This arrangement is used to keep file sizes more manageable and
avoid collapses observed with tools such as QGIS which, as of version 3.34.6 (April 2024), become effectively unusuable on data such as 
100 GB virtual rasters with a dozen bands. Subsequent processing, such as treetop identification, may load bands from both data and
diagnostic raster tiles.

### Integration with other tools
Clouds exists primarily to provide performance or processing capabilities which aren't available with established tool ecosystems such as [R](https://www.r-project.org/), 
[Julia](https://julialang.org/), and [OSGEO](https://www.osgeo.org/) ([GDAL](https://gdal.org/), [QGIS](https://qgis.org/)). In principle, 
interactions between Clouds and other packages consist of handing off data files in well established, interoperable formats such as LAS,
GeoTIFF, and GeoPackage. In practice, interoperability tends to be complex in its details and Clouds does the best it can within available
development resources. In particular,

- LAS specification compliance is often poor, resulting in malformed .las (and .laz) files or tools that fail to work unless a file happens
  fit within the specification's grey zones in a particular way. Clouds tries to work around errors, such as indicating an incorrect number 
  of variable length records (VLRs) in the LAS header, and quirks of common implementations, such as requiring two padding bytes between the 
  VLRs and point data, as best it can.
- GDAL's design doesn't always match well to its actual uses. Because one of Clouds' major workflows is generating raster tiles from aerial
  LiDAR flights, Clouds interacts extensively with GDAL's [GeoTIFF](https://gdal.org/drivers/raster/gtiff.html) and [virtual raster](https://gdal.org/drivers/raster/vrt.html) 
  drivers. However, generation and manipulation of a virtual raster's .vrt is only part of setting up a virtual raster. Clouds does not
  (currently) get involved with pyramid generation.

QGIS raster layer properties, as of the 3.34 LTR, include basic support for generating pyramids at GDAL's default power of two levels (half of 
a virtual raster's dimensions, a quarter, an eighth...). [`gdaladdo`](https://gdal.org/programs/gdaladdo.html), available from the OSGeo shell
installed with QGIS (or from any other GDAL installation), provides more granular control and incremental update of pyramid .ovr files when a 
tile is changed. However, as of GDAL 3.9, `gdaladdo` doesn't support wildcard inputs and thus likely be scripted over a virtual raster's tiles.
It's also not well understood which pyramid levels, as well as which levels to generate for the virtual raster as a whole versus as individual 
files at tile level, produce the greatest usuability in QGIS is not well understood. Anecdotally, power of two level spacing may be more dense
than is required and generating a couple lower levels (e.g. 4 and 8 or 3 and 10) per tile and then a limited resolution pyramid for the virtual 
raster as a whole (perhaps level 16 or 20) may be a reasonable starting point for minimizing QGIS render times for flights in the 20,000–50,000 
ha range. Roughly 100 MB seems reasonably practical for the whole raster .ovr. (Also, QGIS tends to leak render threads so, if rendering becomes
less responsive, check whether QGIS is keeping one or more cores active at idle. If it is, those are probably lost threads. A QGIS restart is
required to clear them.)

GDAL tends to to set `BlockYSize="1"` in .vrt files in contradiction to [GDAL's own guidance](https://gdal.org/drivers/raster/vrt.html), 
resulting in slower rendering in QGIS. Clouds can't really do anything about this, though it adheres to GDAL guidance, offers `Remove-VrtBlockSize`
to remove block sizes, and leaves GDAL free to pick up its [configuration options](https://gdal.org/user/configoptions.html).

### No data values
No data values are detected and ignored for all rasters and, in general, propagate. For example, a no data value in either the digital 
surface or digital terrain model results in a no data value for the canopy height model. Generation of pit free surface models or other
types of no data filling are therefore not required, though alternate approaches fitting canopy models ([Paris and Bruzzone 2015](https://doi.org/10.1109/TGRS.2014.2324016)) 
will likely be beneficial on sparse point clouds. NaN (not a number) is used as the default no data value for floating point bands. For
signed integer bands the most negative value (-128, -32,768, -2,147,483,648, or -9,223,372,036,854,775,808 for 8, 16, 32, and 64 bit
values) is the default no data and, for unsigned integers, the most positive value is used (255, 65,535, 4,294,967,295, or 
18,446,744,073,709,551,615).

No data values can differ among tiles of virtual rasters, often necessarily so when tiles are sharded into files of different data types 
or when tile data types are heterogenous. If raster bands are converted to wider types (for example, bytes to 16 bit unsigned integers) 
no data values are left unchanged but, on conversion to narrower types, no data values are propagated. When it is unavoidable, data is 
altered slightly to allow propagation, most commonly by reducing values of 65535 in 16 bit unsigned image bands to 65534 so that 65535
can be used as the no data value.

### Maximizing LiDAR processing throughput
Clouds is IO bound when .las tiles are being read from hard drives, hard drive RAID configurations typical of high-end desktops or workstations, 
or from flash drives with moderate bandwidth connections such as USB 3 gen 1 (5 Gb/s), gen 2 (10 GB/s), or SATA III (6 Gb/s). Unless a cmdlet 
performs minimal computation, Clouds is most likely to be compute bound when .las files are read from NVMe drives or when working with raster data.
The exact balance between IO and compute throughput depends on drive and CPU core capabilities, Clouds' algorithmic efficiency and code optimization
status, the amount of DDR available relative to the needs of the specific data processing being done, the state of the data structures used for 
processing, and the extent to which Clouds can circumvent performance bottlenecks in GDAL and the operating system. Since the overhead of idle worker 
threads is small, performance disavantages tend to be negligible when compute throughput exceeds IO. Memory pressure tends to occur when IO exceeds 
compute throughput, both because IO loads point clouds into memory until the tile limit (`-MaxPointTiles`) is reached and because each each additional 
compute thread allocates additional working data structures.

Currently, Clouds is limited to processing tiles in the same rowwise order as GDAL uses for rasters, meaning grids of tiles in typical projected 
coordinate systems (+x is east and +y is north) are traversed rowwise from north to south and from west to east in each row. When tiles can be
processed independently of each other, this loading order usually has little effect as memory requirements are primarily a function of the
number of active threads (Clouds commonly defaults to one thread per core, though some cmdlets benefit substantially from hyperthreading and
therefore default to more than one thread per core—see each cmdlet's help for details). When processing needs to access data in neighboring
tiles, each previous east-west row of tiles is retained until the next row is completed. In such cases the width of the area of interest can
control total memory requirements. As Clouds does not currently support east to west, rather than north to south traversal, memory
requirements can be unnecessarily elevated for study areas whose east-west extent is larger than their north-south extent (changing traversal 
direction based on tile grid extent is not especially difficult but this feature hasn't gotten implementation priority as yet).

Clouds' requirements on the amount of DDR physically installed in a system therefore scale with the size of the study area, density of the point
clouds, resolution chosen for rasters, and the number of active CPU cores. Clouds assumes DDR was purchased with the intent of using it to make
data processing efficient and, as a rule of thumb, 8 GB per core is most likely sufficient to avoid memory constraints on performance. If point 
cloud resolution resolution is high enough and the study area large enough, then Clouds' memory requirements will exceed the amount of DDR available
on desktop platforms (128 GB for DDR4 and, at time of writing, 192 GB for DDR5 UDIMMs). DDR exhaustion has not been much of an issue on the projects
Clouds is being developed with but memory management will receive additional attention if need arises.

Perhaps the main consideration in LiDAR processing throughput is the ratio between IO and compute rates. For example, if a single core can read
or write data at 2 GB/s or, if not in an IO phase of the workload, can do the needed processing computations on 300 MB of data per second, then 
an eight core processor achieves a good IO to compute throughput balance with one core doing IO and the other seven cores doing compute. Similarly, 
a 16 core CPU would tend to have two cores doing IO and 14 doing compute. It's usually undesirable to affinitize any particular core or thread to
a particular task. Even when all cores have the same design (AMD Zen 3 and 4 desktop parts, for example), individual cores will boost to different
frequencies depending on the core performance level, total socket power demand, thermals, and die to die variation in fabrication. Processing rates 
therefore vary on a per core basis and, additionally, a core's throughput is likely to vary somewhat over time as its boost level shifts and as it 
experiences different access speeds to L3 cache, DDR main memory, and cross-thread synchronization mechanisms such as locks. Thread throughput 
varies somewhat as well, as the operating system's scheduler may decide to move threads between cores. Processing priorities likely also shift. 
For example, when processing begins no point clouds have been read and, therefore, cores have nothing to do but IO. Conversely, if a row of 
interdependent tiles has been fully read and all of the row's intermediate processing is done, then it's desirable to complete the row's processing 
and write tiles' outputs to disk to release memory for processing subsequent rows. In the case of a 12 core CPU in the example above, it's likely 
that at any given time one thread would running IO, 10 threads would be doing computations, and the twelfth slot would switch back and forth 
IO and computation depending on whether it was higher priority to load more tiles or to complete processing of already loaded tiles. However, 
if the data source is a single hard drive, then limiting IO to a single thread is desirable. If multiple threads attempt to perform concurrent IO 
it's very likely they'll contend for the actuator, decreasing overall performance. Also, AMD processors with both standard and dense cores, 
multi-CCD processors with 3D vcache on one CCD, and Intel processors with P- and E-cores are likely to exhibit greater asymmetries in processing 
rates, both among cores and when the operating system chooses to move threads between cores.

Clouds cmdlets therefore favor fully asynchronous parallelism where, when a thread completes a processing step on a tile, it considers current 
workload status and determines which step would by most useful to start on next. This design attempts to maximize throughput by fully utilizing 
all available resources. If more than one cmdlet, or if other workloads need to run concurrently, utilization can be restricted via `-DataThreads`
and other throttling parameters.

At present, Clouds does not try to schedule reads optimally across drives. The current assumption is all tiles to be read are either 1) within a
single volume, either a single disk or one RAID, or 2) if tiles are located on multiple volumes that the tiles are striped across volumes in
the same north to south, west to east spatial order that the tile grid is traversed in. In the latter case, Clouds assumes the volumes' read 
speeds are well matched to the striping layout and thus that tile loading remains well balanced within the number of tiles allowed to be
simultaneously loaded in memory. In general, this means multi-volume data layouts should likely span a set of JBOD hard drives with similar
performance characteristics (including volumes matching single actuators in multi-actuator drives) or JBOD NVMes having similar capabilities.

A few other performance details are notable.

- Synchronous IO in Windows commonly reaches maximum speeds of 1–2 GB/s per thread, though optimizations in Clouds manage 2.7 GB/s where the
  algorithmic shape of the data processing being done makes it feasible to do so (4.0 GB/s has been demonstrated but specializing threads for 
  maximum IO transfer rates tends to reduce overall throughput). While drive benchmarking tools such as [DiskSpd](https://github.com/microsoft/diskspd) 
  (of which [CrystalDiskMark](https://crystalmark.info/en/software/crystaldiskmark/) uses an older version internally) and .NET IO benchmarks 
  reach transfer rates of 7 and 14 GB/s for PCIe 4.0 x4 and 5.0 x4 NVMe drives, benchmarks need only move data into or out of memory. In point 
  cloud processing workloads, data is read off drives to be used and data being written has to be generated, both of which require computations 
  and additional DDR transfers not present in benchmarking. Profiling of Clouds shows ±20% or more variations in throughput depending on selection 
  among cmdlet implementation tradeoffs and the number of threads running. Absent substantial RAM overclocking, desktop processors' dual channel 
  bandwidth to DDR4 and memory access efficiency is increasingly likely to become a limiting factor as drive transfer rates approach 4–5 GB/s. 
  With DDR5, throughput upper bounds of 12+ GB/s appear likely but have not been tested due to lack of PCIe 5.0 x4 or raided 4.0 x4 drives. 
  Monitoring tools, such as [HWiNFO64](https://www.hwinfo.com/), can be helpful  in tracking DDR and drive transfer rates.
- Drive characteristics and IO coding patterns alter the memory bandwidth demanded by a given IO transfer rate. While little data is available,
  IO to memory ratios have been measured to differ by a factor of 1.5 between NVMes, potentially leading to DDR differences of 10+ GB/s and drive
  transfer rate differences of 1+ GB/s. In application code, such as Clouds, IO is implemented as synchronous or asynchronous with various 
  buffering strategies. Clouds uses synchronous IO as, at least on Windows 10 22H2, it offers a lower DDR:IO ratio and higher throughputs. This 
  requires more threads than an asynchronous approach but, as Clouds does not use more threads than the processor has (and often doesn't use more 
  threads than the processor has cores), the additional threads' cost is negligible. .NET buffered IO can be faster than unbuffered IO and is 
  officially supported but comes with the penalty that amoritizing .NET call overhead requires additional DDR bandwidth to copy data between 
  application and .NET buffers. Apparently as a result, unbuffered IO becomes faster than buffered at higher transfer rates. With desktop hardware 
  current as of mid-2024, unbuffered's been observed to transition to faster at roughly 40% of theoretical DDR bandwidth, which corresponds to IO
  rates in the range of 3.5–6.2 GB/s depending on the workload.
- The optimal number of threads also varies with workload, with cmdlet, IO type, input file content and size, the number of files to process, and 
  hardware capabilities all significantly influencing throughput. Requesting Clouds use more than the optimal number of threads usually reduces
  throughput as different threads contend for system resources. While seek induced reductions in a hard drive actuator's transfer rate are perhaps
  the most obvious (and audible), contention occurs at most other levels as well. Perhaps the most significant of these is L3 cache spilling, where
  frequent cache evictions from a group of cores with shared L3 cache that's carrying too many threads result in substantial increases in DDR 
  bandwidth consumption. Overall throughput often declines in such circumstances as cores are stalled waiting for data to be fetched back into the 
  cache hierarchy. In such situations it's faster and more energy efficient to leave some cores idle. Note also it may not be possible to find a
  thread count fully utilizing an NVMe drive. The greater the throughput per thread, the more likely it is the optimum is a difficult to implement
  fractional number of threads.
- Clouds' peformance is often constrained by GDAL, which saturates at ~3 GB/s throughput for raster operations and imposes single threaded 
  vector writes (see below). GDAL access to layer metadata is also fairly high latency and, while Clouds applies numerous threads 
  as a mitigation (`-MetadataThreads`), throughput remains low. At least on desktop hardware and as of GDAL 3.9, increasing `-MetadataThreads` 
  beyond 24–32 somewhat reduces throughput.
- IO througput above roughly 1 GB/s tend to be stressful to .NET memory management. Best practices are used for object pooling and large object
  heap offloading (in general, data is pooled but metadata such as .las headers and raster band structure is not) but it can take longer for the 
  garbage collector to work through tens of gigabytes of first generation, second generation, and large objects than it does to run a cmdlet. 
  Cmdlets may therefore request a second generation collection and compaction of the managed heap just before they exit. Windows reports process 
  memory consumption based on heap extent so, prior to compaction, Clouds cmdlets may appear to be using substantially more memory than they actually 
  are.
- Windows does not appear to have a documented, programmatic, non-administrative mechanism for determining the RAID level of arrays created with
  Disk Management (dynamic disks). As a result, Clouds assumes dynamic disks are RAID0 (striped) volumes with read speeds slow enough a single 
  read thread can fully utilize the array. For RAID1 (mirrored volumes) or RAID5 (parity), setting `-ReadThreads` to the number of data copies 
  present will likely result in full utilization of hard drive or SATA SSD based RAIDs. Use of `-ReadThreads` is not necessary with Storage 
  Spaces as Windows reports a virtual disk's number of data copies.
- Windows also lacks a way of programatically querying a hard drive's number of actuators. As RAID is usually configured to use multi-actuator 
  drives and multi-actuator drives are uncommon, Clouds does not maintain a list of multi-actuator drive models or attempt to map the disk volume 
  arrangements being used to actuators. If Exos 2X18 halves or similar hardware pairings are being used in JBOD with tiles striped across the 
  halves then `-ReadThreads 2` allows Clouds to operate both actuators simultaneously.

### Limitations
Currently, each rasterization cmdlet performs its own independent read of point cloud tiles. With hard drives and moderate bandwidth SSDs 
this very likely means tile processing bottlenecks on the drive. Scripting cmdlets sequences and letting them running unattended mitigates 
read speed limitations. However, the redundant reads still accumulate towards hard drives' annual workload rate limits. Also, because each
cmdlet invocation is independent, Clouds has no mechanism for learning optimal thread allocations and IO patterns based on what it's asked
to do. In lieu, cmdlets ask `HardwareCapabilities` for estimates of optimal drive interactions and attempt to set reasonable defaults based
on profiling during development. However, it's likely use of `-DataThreads`, `-ReadThreads`, and other settings can create configurations
with higher than default throughput by accounting for specifics of individual hardware. `HardwareCapabilities` makes best case assumptions 
of drive throughput and uses a simple model for workload interactions with total DDR bandwidth. Memory timings are ignored, as are differences
among processors' DDR controllers and internal IO structures.

GeoTIFFs with different datatypes in different bands are perhaps best described as semi-supported within the [OSGEO](https://www.osgeo.org/) 
toolset. Currently GeoTIFFs are written with all bands being of the same type using GDAL's default no data profile that requires all bands
have the same no data value. Where appropriate, bands of different types are split off into diagnostic .tifs accompanying the main .tif files
output by `Get-Orthoimages` and `Get-Dsm`. Investigation is needed to assess use of other GeoTIFF profiles, heterogenous band types within
GeoTIFFs, and raster formats with more complete support for mixed band types.

Also,

- All raster testing and use is currently coordinate system aligned (unrotated) with negative cell heights. Clouds is rotation and positive
  cell height aware but not all code paths support such rasters and the lack of testing likely means edge cases aren't completely handled.
- Currently NAVD88 is the only well known vertical coordinate system. `Convert-CloudCrs`'s imputation of missing vertical coordinate systems
  therefore doesn't behave correctly outside of North America.
- Supported raster data types are real values (8, 16, 32, and 64 bit signed and unsigned integers, single and double precision floating point).
  While GDAL also supports complex values and two other cell data types, Clouds does not.
- QGIS will modify virtual rasters (.vrt files) by adding or updatating band metadata and approximate histograms when a project is closed, often 
  following changes to symbology. When QGIS does this it inserts `BlockYSize="1"` attributes on all the tiles listed in the .vrt or replaces
  existing `BlockYSize` values with 1, dramatically reducing performance for virtual rasters composed of GeoTIFF tiles once the QGIS project 
  is reopened. `Remove-VrtBlockSize` can be used to strip out the reintroduced `BlockXSize` and `BlockYSize` attributes (find/replace on the 
  .vrt in a text editor does the same thing).
- In certain situations GDAL bypasses its callers and writes directly to the console. The common case for this is `ERROR 4` when a malformed 
  dataset (GIS file) is encountered. If error 4 occurs during a raster write Clouds catches it and tries to recreate the raster, which typically 
  succeeds,but Clouds has no ability to prevent GDAL's error message from appearing in PowerShell.

### Dependencies
Clouds is a .NET 9 assembly which includes C# cmdlets for PowerShell Core. It therefore makes use of both the System.Management.Automation
nuget package and the system's PowerShell Core installation, creating a requirement the PowerShell Core version be the same or newer than 
the nuget's. If Visual Studio Code is used for PowerShell Core execution then corresponding updates to Visual Studio Code and its PowerShell 
extension are required.

Clouds relies on GDAL for GIS operations. This imposes performance bottlenecks in certain situations, notably where GDAL's design forces single 
threaded write transactions on large GeoPackages and its raster tile cache (for groups of pixels within individual rasters, not to be confused 
with virtual raster tiles) imposes memory and DDR bandwidth overhead. Clouds mitigates the former by regularly committing GeoPackage 
transactions for GDAL's SQL background thread to process in parallel within the [restricted multithreading](https://gdal.org/user/multithreading.html) 
GDAL supports (it's possible GDAL's underlying datalayers are capable of concurrent transactions on a single dataset is supported but, 
as of GDAL 3.9.1, it's not documented to be and thus has to be assumed unsafe). With respect to GDAL's tile cache, specifying [`GTIFF_DIRECT_IO=YES`](https://gdal.org/drivers/raster/gtiff.html) 
as a [GDAL option](https://gdal.org/user/configoptions.html) bypasses caching but does not appear worth using as direct GeoTIFF 
performance is consistently lower than going through cache. (GDAL's C# interface also lacks support for `Span<T>` and remains missing 
nullability annotations.)

Clouds is developed and tested using current or near-current versions of [Visual Studio Community](https://visualstudio.microsoft.com/downloads/).
Clouds is only tested on Windows 10 22H2 but should run on any .NET supported platform once an implementation of `HardwareCapabilities` 
necessarily operating system specific detection of DDR bandwidth and drive types is provided. RAID0, RAID1, and RAID10 receive limited 
attention and parity based RAIDs have not been tested.