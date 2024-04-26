### Overview
A research codebase with an ad hoc collection of PowerShell cmdlets for working with remotely sensed data, primarily point clouds.

- Get-Dsm: get digital surface, canopy maxima, and canopy height models from a set of point cloud tiles with supporting information
- Get-GridMetrics: get z, intensity, and other common grid metrics from a set of point cloud tiles
- Get-Orthoimages: get 16 bit RGB+NIR orthoimages with LiDAR intensity bands from point clouds
- Get-TreeTops: find treetop candidates in a digital surface or canopy height model

The cmdlets are multithreaded at the tile level with defaults set favoring use cases with point cloud datasets are large enough to be stored
on 3.5 inch drives and don't fit in memory while DSMs and DTMs (digital surface and terrain models) are stored on faster drives (NVMe, SSD). 
Default read thread counts and IO capabilities scale accordingly. [LAS](https://www.asprs.org/divisions-committees/lidar-division/laser-las-file-format-exchange-activities) 
and [GDAL](https://gdal.org/) file formats are supported, though GDAL testing is limited to GeoPackage and GeoTIFF. In cases where 
directories are searched for data tiles .las and .tif are the default file extensions.

DRAM utilization varies with dataset structure and with parallelism. AMD Zen 3 or newer processors with 12–16 cores and 64–128 GB of DDR are 
assumed as typical hardware. Development and testing extend to point cloud tile sets up to 2 TB with most such processing tasks fitting within 
64 GB DDR, though 96 or 128 GB is preferable. Tile processing rates depend on drive and processor core capabilities but .las read speeds can 
exceed 2.0 GB/s per thread. AVX, AVX2, and FMA instructions are used at times. AVX10/256 and AVX10/512 are not currently supported.

Code is currently pre-alpha and provided as is. Typically, the head commit should compile and pass unit tests but this isn't guaranteed. 
APIs are volatile and subject to breaking changes.

### Supporting cmdlets
Supporting tools are

- Get-LocalMaxima: get local maxima radii in a DSM (or DTM)
- Get-ScanMetrics: similar to Get-GridMetrics but reports on data acquisition (scan angle and direction, noise and withheld points, flags)
- Get-TreeSize: get sizes of directories on disk, including some common file types (filesystem trees, not actual trees)
- Get-LasInfo: read a .las or .laz file's header

### Dataset structure
A LiDAR dataset is assumed to consist of

- .las point cloud tiles
- .tif digital terrain models
- .tif raster tiles generated from the point cloud tiles at fairly high resolution (typically 10 cm to 1 m, Get-Dsm, Get-Orthoimages)
- .tif single rasters covering the study area at lower resolution (typically 10–30 m, Get-GridMetrics, Get-ScanMetrics)
- .gpkg vector tiles of treetops located in a digital surface model (Get-TreeTops)

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

### Limitations
Currently each rasterization cmdlet performs its own independent read of point cloud tiles. With 3.5 inch drives this likely means tile
processing bottlenecks on the drive. Scripting cmdlets sequences and letting them running unattended mitigates read speed limitations. 
However, the redundant reads still accumulate towards drives' annual workload rate limits.

GeoTIFFs with different datatypes in different bands are perhaps best described as semi-supported within the [OSGEO](https://www.osgeo.org/) 
toolset. Currently GeoTIFFs are written with all bands being of the same type using GDAL's default no data profile that requires all bands
have the same no data value. Investigation is needed to assess use of other profiles and heterogenous band types.

### Dependencies
Clouds is a .NET 8.0 assembly which includes C# cmdlets for PowerShell Core. It therefore makes use of both the System.Management.Automation
nuget package and the system's PowerShell Core installation, creating a requirement the PowerShell Core version be the same or newer than 
the nuget's. If Visual Studio Code is used for PowerShell Core execution then corresponding updates to Visual Studio Code and its PowerShell 
extension are required.

Clouds relies on GDAL for GIS operations. This imposes performance bottlenecks in certain situations, mainly where GDAL forces single threaded 
write transactions on large GeoPackages.

Clouds is developed using current or near-current versions of [Visual Studio Community](https://visualstudio.microsoft.com/downloads/) 
edition. Clouds is only tested on Windows 10 but should run on any .NET supported platform.

### Limitations
Current constraints are

- The user has to know what thread counts to specify for optimal performance. Thread selection is automatable but requires performance 
  characterization and drive capability identification.
- There's not a way to attach multiple outputs to a single .las file read, likely resulting in longer runtimes and greater drive workload 
  rates due a given .las file being read multiple times by different cmdlets.