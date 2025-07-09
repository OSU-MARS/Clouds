# This script illustrates cmdlet use. Paths need to be changed to files available for the area of interest.
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net9.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net9.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native') # for GDAL

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

$dataPath = "D:\Wind River\Munger"


## georeferencing and alignment
# TODO
# Register-Cloud -Las "$dataPath\strip 2\MUN 005.las" -Lat 45.8 -Long -121.9 -Z 359.7 -NudgeY 2.6 -NudgeX -10 -RotationXY 16 -SourceID 5 -FallbackDate 2025-06-03 -RepairClassification -RepairReturn


## rasterization to mean intensity slices for stem mapping
Export-Slices -Las "$dataPath\strip 2\MUN 005 registered.las" -Trim 90 -Dtm "$dataPath\wasco_b_2015\dtm\wasco_b_2015_dtm_24 utm10n.tif"