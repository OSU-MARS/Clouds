$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net7.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net7.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native')

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

Get-Treetops -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\s03450w06540.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM\s03450w06540.tif" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops\s03450w06540.gpkg")) -Verbose

#Get-Treetops -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\*.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops")) -Verbose