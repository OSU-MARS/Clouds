# This script illustrates cmdlets for raster processing. Paths need to be changed to files available for the area of interest.
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net9.0"))
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net9.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native') # for GDAL

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

## slope and aspect generation from DSM, single tile
Get-DsmSlopeAspect -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3\s04200w06840.tif" -Verbose

# slope and aspect generation from DSM, all tiles
# Most recent perf data: 9900X + DDR5-5600, SN850X on CPU lanes, 561 DSM tiles totaling 25 GB, .NET 8
# data threads    slope-aspect + .vrt time, s    DDR bandwidth, GB/s read + write
# 12              16                             15 + 7.5
# 24              11                             23 + 11
Get-DsmSlopeAspect -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3" -Vrt -Verbose
