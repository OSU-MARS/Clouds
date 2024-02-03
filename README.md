### Overview
A research codebase with an ad hoc collection of PowerShell cmdlets for working with remotely sensed data, primarily point clouds.

- Get-Dsm: get a digital surface model from a set of point cloud tiles
- Get-GridMetrics: get z, intensity, and other grid metrics from a set of point cloud tiles
- Get-Orthoimages: get 32 bit RGB+NIR orthoimages with LiDAR intensity bands from point clouds
- Get-TreeTops: find treetop candidates in a canopy height or digital surface model

Currently, [LAS](https://www.asprs.org/divisions-committees/lidar-division/laser-las-file-format-exchange-activities) and [GDAL](https://gdal.org/)
file formats are supported, though GDAL testing is limited to GeoPackage and GeoTIFF.

### Supporting cmdlets
Supporting tools are

- Get-ScanMetrics: similar to Get-GridMetrics but reports on data acquisition (scan angle and direction, noise and withheld points, flags)
- Get-LasInfo: read a .las or .laz file's header
- Get-TreeSize: get sizes of directories on disk, including some common file types (filesystem trees, not actual trees)

### Dependencies
Clouds is a .NET 8.0 assembly which includes C# cmdlets for PowerShell Core. It therefore makes use of both the System.Management.Automation
nuget package and the system's PowerShell Core installation, creating a requirement the PowerShell Core version be the same or newer than 
the nuget's. If Visual Studio Code is used for PowerShell Core execution then corresponding updates to Visual Studio Code and its PowerShell 
extension are required.

Clouds relies on GDAL for GIS operations. This imposes performance bottlenecks in certain situations, including GDAL forcing single threaded 
write transactions on large GeoPackages and being slow operations with 32 bit integer raster images due to lack of C# bindings for 16 bit
unsigned GeoTIFFs. A workaround for the latter is to transcode rasters from 32 bit down to 16 bit with `terra::rast()` and `writeRast()` (in 
R), `gdal_translate`, or similar tools.

Clouds is developed using current or near-current versions of [Visual Studio Community](https://visualstudio.microsoft.com/downloads/) 
edition. Clouds is only tested on Windows 10 but should run on any .NET supported platform.