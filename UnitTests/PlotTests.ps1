# This script illustrates cmdlet use. Paths need to be changed to files available for the area of interest.
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net10.0"))
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net10.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native')

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))
$dataPath = "D:\FIA\JVA"

# defaults: inverse linear distance, no smoothing
Get-Dtm "$dataPath\las\*.las" -Dtm "$dataPath\dtm" -Snap -Verbose

$pointCounts = Get-PointClassifications "$dataPath\las\*.las"
$pointCounts | Export-Csv -Path ([System.IO.Path]::Combine((Get-Location), "$dataPath\point counts.csv"))