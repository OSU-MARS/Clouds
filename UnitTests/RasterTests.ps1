# This script illustrates cmdlets for raster processing. Paths need to be changed to files available for the area of interest.
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net8.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net8.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native') # for GDAL

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

## slope and aspect generation from DSM, single tile
Get-DsmSlopeAspect -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3 beta\s04200w06840.tif" -Verbose

# slope and aspect generation from DSM, all tiles
Get-DsmSlopeAspect -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3 beta" -Smooth -Vrt -Verbose
