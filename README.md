### Overview
A research codebase with an ad hoc collection of PowerShell cmdlets for working with remotely sensed data, primarily point clouds.

- `Get-Dsm`: get digital surface, canopy maxima, and canopy height models from a set of point cloud tiles with supporting information
- `Get-GridMetrics`: get z, intensity, and other common grid metrics from a set of point cloud tiles
- `Get-Orthoimages`: get 16 bit RGB+NIR orthoimages with LiDAR intensity bands from point clouds
- `Get-TreeTops`: find treetop candidates in a digital surface or canopy height model

The cmdlets are multithreaded at the tile level with defaults set favoring use cases with point cloud datasets are large enough to be stored
on 3.5 inch drives and don't fit in memory while DSMs and DTMs (digital surface and terrain models) are stored on faster drives (NVMe, SSD). 
Default read thread counts and IO capabilities scale accordingly. [LAS](https://www.asprs.org/divisions-committees/lidar-division/laser-las-file-format-exchange-activities) 
and [GDAL](https://gdal.org/) file formats are supported, though GDAL testing is limited to GeoPackage and GeoTIFF. In cases where 
directories are searched for data tiles .las and .tif are the default file extensions.

DRAM utilization varies with dataset structure and with parallelism. AMD Zen 3 or newer processors with 12–16 cores and 64–128 GB of DDR are 
assumed as typical hardware. Development and testing extend to point cloud tile sets up to 2 TB with most such processing tasks fitting within 
64 GB DDR, though 96 or 128 GB is preferable. Tile processing rates depend on drive and processor core capabilities but .las read speeds of 2.0 
GB/s are sustainable. AVX, AVX2, and FMA instructions are used at times. AVX10/256 and AVX10/512 are not currently utilized. QGIS
(as of the 3.34 LTR) is slow to work with larger virtual rasters, which Clouds can't really do anything about, though using `Get-Vrt` to generate
.vrt files with a minimal set of bands and tiles offers a mitigation.

Code is currently pre-alpha and provided as is. Typically, the head commit should compile and pass unit tests but this isn't guaranteed. 
APIs are volatile and subject to breaking changes.

### Supporting cmdlets
Supporting tools are

- `Get-LocalMaxima`: get local maxima radii in a DSM (or DTM)
- `Get-ScanMetrics`: similar to `Get-GridMetrics` but reports on data acquisition (scan angle and direction, noise and withheld points, flags)
- `Get-Vrt`: a [gdalbuildvrt](https://gdal.org/programs/gdalbuildvrt.html) alternative supporting sharded tile sets with fixes for other limitations
- `Get-LasInfo`: read a .las or .laz file's header
- `Register-Cloud`: set .las files' origin, coordinate system, and source ID
- `Convert-CloudCrs`: reproject .las files to a new coordinate system, adding a vertical coordinate system if one is not present
- `Get-TreeSize`: get sizes of directories on disk, including some common file types (filesystem trees, not actual trees)
- `Repair-NoisePoints`: mark z outliers as high and low noise relative to DTM (useful for that one point in low Earth orbit)
- `Export-VrtBands`: work around QGIS performance limitations in displaying virtual rasters by extracting subsets of the bands in a .vrt
- `Remove-VrtBlockSize`: work around QGIS's tendency to further degrade its performance displaying virtual rasters (see below)
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

Virtual rasters are traversed rowwise. This is unimportant when output tile generation uses only data from a single corresponding source tile.
When outputs need to consider data in adjacent tiles, currently entire rows are unlocked and freed. Memory use therefore scales with the
width of the study area, which is helpful to areas that are longer north-south than they are east-west, neutral to square areas, and 
disadvantageous to areas which are wide east-west. More intelligent unlocking, freeing, and the ability to traverse wide areas columnwise
have not been implemented.

Also,

- Currently NAVD88 is the only well known vertical coordinate system. `Convert-CloudCrs`'s imputation of missing vertical coordinate systems
  therefore doesn't behave correctly outside of North America.
- Supported raster data types are real values (8, 16, 32, and 64 bit signed and unsigned integers, single and double precision floating point).
  While GDAL also supports complex values and two other cell types, Clouds does not.
- The user has to know what thread counts to specify for optimal performance. Thread selection is automatable and cmdlets make an effort to 
  set reasonable defaults but entirely robust choices require performance characterization and drive capability identification beyond the scope 
  of current work.
- Read speeds above roughly 1 GB/s tend to be stressful to .NET memory management. Best practices are used for object pooling and large object
  heap offloading but it can take longer for the garbage collector to work through tens of gigabytes of first generation, second generation, and
  large objects than it does to run a cmdlet. Cmdlets may therefore request a second generation collection and compaction of the managed heap
  just before they exit.
- QGIS will modify virtual rasters (.vrts) by adding or updatating band metadata and approximate histograms when a project is closed, often 
  following changes to symbology. When QGIS does this it inserts `BlockYSize="1"` attributes on all the tiles listed in the .vrt or sets existing 
  `BlockYSize` values with 1, dramatically reducing performance for virtual rasters composed of GeoTIFF tiles once the QGIS project is reopened.
  Clouds can't do much about this QGIS-GDAL behavior but `Remove-VrtBlockSize` can be used to strip out the reintroduced `BlockXSize` and 
  `BlockYSize` attributes (find/replace in a text editor also works).

In certain situations GDAL bypasses its callers and writes directly to the console. The common case for this is `ERROR 4` when a malformed dataset
(GIS file) is encountered. If error 4 occurs during a raster write Clouds catches it and tries to recreate the raster (which typically succeeds).