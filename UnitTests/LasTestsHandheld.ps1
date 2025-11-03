# This script illustrates cmdlet use. Paths need to be changed to files available for the area of interest.
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net9.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net9.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native') # for GDAL

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))


## georeferencing and alignment
# handheld cloud registration and DSM generation
$scanDir = [System.IO.Path]::Combine($env:USERPROFILE, "PhD\data\McDonald-Dunn\Stand 50603")
Register-Cloud -Lat 44.64663 -Long -123.27204 -Z 429.3 -HorizontalEpsg 6556 -RotationXY -41.5 -FallbackDate ([System.DateOnly]::new(2024, 5, 11)) -SourceID 1 -Las ([System.IO.Path]::Combine($scanDir, "scan 1\scan 1 RGB+class.las"))
Register-Cloud -Lat 44.64663 -Long -123.27204 -Z 429.3 -HorizontalEpsg 6556 -RotationXY -25 -FallbackDate ([System.DateOnly]::new(2024, 5, 11)) -SourceID 2 -Las ([System.IO.Path]::Combine($scanDir, "scan 2\scan 2 RGB+class.las"))
Register-Cloud -Lat 44.64663 -Long -123.27204 -Z 429.0 -HorizontalEpsg 6556 -RotationXY -22 -NudgeX -1 -NudgeY 2 -FallbackDate ([System.DateOnly]::new(2024, 5, 11)) -SourceID 3 -Las ([System.IO.Path]::Combine($scanDir, "scan 3\scan 3 RGB+class.las"))
Register-Cloud -Lat 44.64663 -Long -123.27204 -Z 429.3 -HorizontalEpsg 6556 -RotationXY -25 -NudgeX 5 -FallbackDate ([System.DateOnly]::new(2024, 5, 11)) -SourceID 3 -Las ([System.IO.Path]::Combine($scanDir, "scan 4\scan 4 RGB+class.las"))
Register-Cloud -Lat 44.64800 -Long -123.27089 -Z 429.5 -HorizontalEpsg 6556 -RotationXY -43 -FallbackDate ([System.DateOnly]::new(2024, 5, 11)) -SourceID 5 -Las (([System.IO.Path]::Combine($scanDir, "scan 5\scan 5 RGB+class.las")), ([System.IO.Path]::Combine($scanDir, "scan 6\scan 6 RGB+class.las")))

# rasterization to mean intensity slices for stem mapping

$dataPath = "D:\Wind River\Munger"
Export-Slices -Las "$dataPath\strip 2\MUN 005 registered.las" -Trim 90 -Dtm "$dataPath\wasco_b_2015\dtm\wasco_b_2015_dtm_24 utm10n.tif"