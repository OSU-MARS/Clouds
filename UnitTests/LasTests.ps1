$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net8.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net8.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native') # for GDAL

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

# cmdlet execution with a .las tile and ABA (area based approach) grid cell definition
# This script illustrates cmdlet use. Paths need to be changed to files available for area of interest.
$gridPath = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\Elliott ABA grid 10 m EPSG 6557.tif"
$gridMetrics = Get-GridMetrics -Cells $gridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points normalized\s03540w067?0.las" -Verbose
$gridMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\s03540w067n0.tif");

# 3x3 tile, 19,044 cell perf benchmark
# case           runtime   cells/s   disk transfer rate, MB/s
# baseline       1m:52s    168.9     ~260
# tiles cached   50s       380.7      0 (~586 implied)
#$gridMetrics = Get-GridMetrics -Cells $gridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points normalized\s037?0w070?0.las" -Verbose

# full run example: 663 LiDAR tiles (1.92 TB of .las files), 1.03 M ABA cells -> 187 MB raster
#$gridMetrics = Get-GridMetrics -Cells $gridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points normalized\*.las" -Verbose
#$gridMetrics = Get-GridMetrics -Cells $gridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points\*.las" -Verbose
#$gridMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\grid metrics 10 m non-normalized.tif");

# DSM generation, single tile
$tile = "s04200w06810" # "s04230w06810" # "s04020w07050"
Get-Dsm -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points\$tile.las" -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM with outlier rejection\$tile.tif" -Snap -Verbose
Get-Dsm -UpperPoints 1 -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points\$tile.las" -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM with outlier rejection\$tile control.tif" -Snap -Verbose
Get-Dsm -UpperPoints 10 -WriteUpperPoints -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points\$tile.las" -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM with outlier rejection\$tile points.tif" -Snap -Verbose

# high resolution grid metrics bootstrapping from DSM (or CHM or DTM) grid
# Just under 4 Mcells => 600 MB float32 .tif @ 57 standard metrics bands.
$tile = "s04200w06810"
$dsmMetrics = Get-GridMetrics -Cells "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\$tile.tif" -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points\$tile.las" -Verbose
$dsmMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\$tile DSM resolution.tif");

# read header from .las or .laz file
$lasFile = Get-LasInfo -Las ([System.IO.Path]::Combine((Get-Location), "PSME LAS 1.4 point type 6.las"))
$lazFile = Get-LasInfo -Las "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\pointz\s03780w06540.laz"
