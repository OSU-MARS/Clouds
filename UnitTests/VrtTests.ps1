$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net8.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net8.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native') # for GDAL

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

# .vrt from single directory with all tile bands
$vrtDirectory = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM testing"
Get-Vrt -TilePaths $vrtDirectory -Vrt ([System.IO.Path]::Combine($vrtDirectory, "Get-Vrt test dsm cmm3 chm.vrt"))

# .vrt with mixed primary and diangostic bands
$vrtDirectory = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM testing"
Get-Vrt -Bands ("dsm", "chm", "sourceIDsurface") -TilePaths ($vrtDirectory, ([System.IO.Path]::Combine($vrtDirectory, "sourceID"))) -Vrt ([System.IO.Path]::Combine($vrtDirectory, "Get-Vrt test dsm chm sourceIDsurface.vrt"))

# .vrt with all primary and diangostic bands
$vrtDirectory = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM testing"
Get-Vrt -TilePaths ($vrtDirectory, ([System.IO.Path]::Combine($vrtDirectory, "z")), ([System.IO.Path]::Combine($vrtDirectory, "nPoints")), ([System.IO.Path]::Combine($vrtDirectory, "sourceID"))) -Vrt ([System.IO.Path]::Combine($vrtDirectory, "Get-Vrt test DSM all.vrt"))