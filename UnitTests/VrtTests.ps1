# This script illustrates cmdlet use. Paths need to be changed to files available for the area of interest.
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net9.0"))
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net9.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native') # for GDAL

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))


# DSM and orthoimage .vrts with all primary and diagnostic bands, complete sampling of band statistics, and logging of all tile statistics
$vrtDirectory = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3"
Get-Vrt -Stats -MinSamplingFraction 1.0 -TilePaths ($vrtDirectory, ([System.IO.Path]::Combine($vrtDirectory, "z")), ([System.IO.Path]::Combine($vrtDirectory, "nPoints")), ([System.IO.Path]::Combine($vrtDirectory, "sourceID"))) -Vrt ([System.IO.Path]::Combine($vrtDirectory, "dsm all.vrt")) -Verbose

$vrtDirectory = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\orthoimage v3"
Get-Vrt -Stats -MinSamplingFraction 1.0 -TilePaths ($vrtDirectory, ([System.IO.Path]::Combine($vrtDirectory, "nPoints")), ([System.IO.Path]::Combine($vrtDirectory, "scanAngle"))) -Vrt ([System.IO.Path]::Combine($vrtDirectory, "orthoimage all.vrt")) -Verbose

# extraction of .vrt bands
# If a .vrt is generated for all bands then bands can be sliced out of it for more performant viewing in QGIS.
$vrtDirectory = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3"
$vrt = [System.IO.Path]::Combine($vrtDirectory, "dsm all.vrt")
Export-VrtBands -Input $vrt -Bands "dsm" -Output ([System.IO.Path]::Combine($vrtDirectory, "dsm.vrt"))
Export-VrtBands -Input $vrt -Bands "cmm3" -Output ([System.IO.Path]::Combine($vrtDirectory, "cmm3.vrt"))
Export-VrtBands -Input $vrt -Bands "chm" -Output ([System.IO.Path]::Combine($vrtDirectory, "chm.vrt"))
Export-VrtBands -Input $vrt -Bands "sourceIDsurface" -Output ([System.IO.Path]::Combine($vrtDirectory, "sourceIDsurface.vrt"))
Export-VrtBands -Input $vrt -Bands ("nAerial", "nGround") -Output ([System.IO.Path]::Combine($vrtDirectory, "dsm density.vrt"))
Export-VrtBands -Input $vrt -Bands ("dsm", "cmm3", "chm", "aerialMean") -Output ([System.IO.Path]::Combine($vrtDirectory, "dsm cmm3 chm aerialMean.vrt"))
Export-VrtBands -Input $vrt -Bands ("dsm", "cmm3", "chm", "aerialMean", "nAerial", "nGround") -Output ([System.IO.Path]::Combine($vrtDirectory, "dsm cmm3 chm aerialMean density.vrt"))

$vrtDirectory = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\orthoimage v3"
$vrt = [System.IO.Path]::Combine($vrtDirectory, "orthoimage all.vrt")
Export-VrtBands -Input $vrt -Bands ("red", "green", "blue") -Output ([System.IO.Path]::Combine($vrtDirectory, "orthoimage.vrt"))
Export-VrtBands -Input $vrt -Bands ("red", "green", "nearInfrared") -Output ([System.IO.Path]::Combine($vrtDirectory, "orthoimage nir.vrt"))
Export-VrtBands -Input $vrt -Bands ("firstReturns", "secondReturns") -Output ([System.IO.Path]::Combine($vrtDirectory, "orthoimage density.vrt"))
Export-VrtBands -Input $vrt -Bands ("intensityFirstReturn", "intensitySecondReturn") -Output ([System.IO.Path]::Combine($vrtDirectory, "orthoimage intensity.vrt"))
Export-VrtBands -Input $vrt -Bands "scanAngleMeanAbsolute" -Output ([System.IO.Path]::Combine($vrtDirectory, "orthoimage scan angle.vrt"))

# remove BlockXSize and BlockYSize attributes
Remove-VrtBlockSize -Vrt "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3 beta\dsm cmm3 chm sourceIDsurface.vrt"

# direct generation of .vrts from single directory with primary bands
$vrtDirectory = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3 beta"
Get-Vrt -Bands "dsm" -TilePaths $vrtDirectory -Vrt ([System.IO.Path]::Combine($vrtDirectory, "dsm.vrt"))
Get-Vrt -Bands "cmm3" -TilePaths $vrtDirectory -Vrt ([System.IO.Path]::Combine($vrtDirectory, "cmm3.vrt"))
Get-Vrt -Stats -TilePaths $vrtDirectory -Vrt ([System.IO.Path]::Combine($vrtDirectory, "dsm cmm3 chm.vrt"))

# direct generation of .vrts from single directory with diagnostic bands
$vrtDirectory = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3 beta"
Get-Vrt -Bands "sourceIDsurface" -TilePaths ([System.IO.Path]::Combine($vrtDirectory, "sourceID")) -Vrt ([System.IO.Path]::Combine($vrtDirectory, "sourceIDsurface.vrt"))

# direct generation of .vrts with mixed primary and diangostic bands
$vrtDirectory = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3 beta"
Get-Vrt -Bands ("dsm", "chm", "sourceIDsurface") -TilePaths ($vrtDirectory, ([System.IO.Path]::Combine($vrtDirectory, "sourceID"))) -Vrt ([System.IO.Path]::Combine($vrtDirectory, "dsm chm sourceIDsurface.vrt"))
Get-Vrt -Bands ("dsm", "cmm3", "chm", "sourceIDsurface") -TilePaths ($vrtDirectory, ([System.IO.Path]::Combine($vrtDirectory, "sourceID"))) -Vrt ([System.IO.Path]::Combine($vrtDirectory, "dsm cmm3 chm sourceIDsurface.vrt"))

# direct generation of .vrts with diagnostic bands
$vrtDirectory = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3 beta"
Get-Vrt -Bands ("nAerial", "nGround") -TilePaths ([System.IO.Path]::Combine($vrtDirectory, "nPoints")) -Vrt ([System.IO.Path]::Combine($vrtDirectory, "nAerial nGround.vrt"))
Get-Vrt -Bands ("layer1", "layer2", "ground", "sourceIDlayer1", "sourceIDlayer2") -TilePaths (([System.IO.Path]::Combine($vrtDirectory, "z")), ([System.IO.Path]::Combine($vrtDirectory, "sourceID"))) -Vrt ([System.IO.Path]::Combine($vrtDirectory, "layer12 ground sourceID12.vrt"))
