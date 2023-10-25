$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net7.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net7.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native') # for GDAL

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

# cmdlet execution with a .las tile and ABA (area based approach) grid cell definition
# This script illustrates cmdlet use. Paths need to be changed to files available for area of interest.
$abaGridPath = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\Elliott ABA grid 20 m EPSG 6557.tif"
$abaMetrics = Get-GridMetrics -AbaCells $abaGridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points normalized\s03540w067?0.las" -Verbose
$abaMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\s03540w067n0.tif");

# 3x3 tile, 19,044 cell perf benchmark
# case           runtime   cells/s   disk transfer rate, MB/s
# baseline       1m:52s    168.9     ~260
# tiles cached   50s       380.7      0
#$abaMetrics = Get-GridMetrics -AbaCells $abaGridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points normalized\s037?0w070?0.las" -Verbose

# full run example: 663 LiDAR tiles (1.92 TB of .las files), 1.03 M ABA cells -> 187 MB raster
#$abaMetrics = Get-GridMetrics -AbaCells $abaGridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points normalized\*.las" -Verbose
#$abaMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\grid metrics 20 m.tif");

# read header from .las or .laz file
$lasFile = Get-LasInfo -Las ([System.IO.Path]::Combine((Get-Location), "PSME LAS 1.4 point type 6.las"))
$lazFile = Get-LasInfo -Las "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\pointz\s03780w06540.laz"
