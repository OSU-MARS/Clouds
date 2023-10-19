$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net7.0"))
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net7.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native')

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

# cmdlet execution with a .las tile and ABA (area based approach) grid cell definition
# Change paths to vailable files for area of interest.
$abaGridPath = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\Elliott ABA grid 20 m EPSG 6557.tif"
#$lasFile = Get-LasInfo -Las $lasPath
$abaMetrics = Get-GridMetrics -AbaCells $abaGridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points normalized\s03450w06540.las" -Verbose
$abaMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\s03450w06540.tif");

# full run example: 663 LiDAR tiles (1.92 TB of .las files), 1.03 M ABA cells
#$abaMetrics = Get-GridMetrics -AbaCells $abaGridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\points normalized\*.las" -Verbose
#$abaMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\grid metrics 20 m.tif");