### Overview
A research codebase with an ad hoc collection of PowerShell cmdlets for working with remotely sensed data, primarily point clouds.

- Get-Dsm: get a digital surface model from a set of point cloud tiles
- Get-GridMetrics: get z, intensity, and other grid metrics from a set of point cloud tiles
- Get-Orthoimages: get 32 bit RGB+NIR orthoimages with LiDAR intensity bands from point clouds
- Get-TreeTops: find treetop candidates in a canopy height or digital surface model

The cmdlets are multithreaded at the tile level but, currently, it's assumed 1) point cloud datasets are large enough to be stored on 3.5 
inch drives and don't in memory while 2) DSMs and DTMs (digital surface and terrain models) are stored on faster drives (NVMe, SSD). Thread 
counts and IO capabilities scale accordingly. [LAS](https://www.asprs.org/divisions-committees/lidar-division/laser-las-file-format-exchange-activities) 
and [GDAL](https://gdal.org/) file formats are supported, though GDAL testing is limited to GeoPackage and GeoTIFF. In cases where 
directories are searched for data tiles GeoTIFF is the default extension.

Code is provided as is. In general, the head commit should compile and pass unit tests but this isn't absolutely guaranteed. APIs are
likely volatile.

### Supporting cmdlets
Supporting tools are

- Get-LocalMaxima: get local maxima radii in a DSM (or DTM)
- Get-ScanMetrics: similar to Get-GridMetrics but reports on data acquisition (scan angle and direction, noise and withheld points, flags)
- Get-TreeSize: get sizes of directories on disk, including some common file types (filesystem trees, not actual trees)
- Get-LasInfo: read a .las or .laz file's header

### Dependencies
Clouds is a .NET 8.0 assembly which includes C# cmdlets for PowerShell Core. It therefore makes use of both the System.Management.Automation
nuget package and the system's PowerShell Core installation, creating a requirement the PowerShell Core version be the same or newer than 
the nuget's. If Visual Studio Code is used for PowerShell Core execution then corresponding updates to Visual Studio Code and its PowerShell 
extension are required.

Clouds relies on GDAL for GIS operations. This imposes performance bottlenecks in certain situations, mainly where GDAL forces single threaded 
write transactions on large GeoPackages.

Clouds is developed using current or near-current versions of [Visual Studio Community](https://visualstudio.microsoft.com/downloads/) 
edition. Clouds is only tested on Windows 10 but should run on any .NET supported platform.