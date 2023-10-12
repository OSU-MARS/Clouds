$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net7.0"))
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net7.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native')

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

# cmdlet execution with a .las tile and ABA (area based approach) grid cell definition
# Change paths to vailable files for area of interest.
$abaGridPath = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\Elliott ABA grid 20 m EPSG 6557.tif"
$lasPath = "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points normalized\s03780w07020.las"
#$lasFile = Get-LasInfo -Las $lasPath
$abaMetrics = Get-GridMetrics -AbaCells $abaGridPath -Las $lasPath -Verbose