$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net7.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net7.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native')

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))
Get-Treetops -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\s04020w06690.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM\s04020w06690.tif" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM\s04020w06690 new.gpkg")) -Verbose

Get-Treetops -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\*.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM")) -Verbose

Get-Treetops -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\s03450w06540.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM\s03450w06540.tif" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM\s03450w06540 new.gpkg")) -Verbose

Get-Treetops -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\s04110w06840.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM\s04110w06840.tif" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM\s04110w06840 new.gpkg")) -Verbose


#
#$reprocessTiles = ("s04320w06660.tif", "s04320w06690.tif", "s04320w06720.tif", "s04320w06750.tif", "s04320w06780.tif", "s04320w06810.tif", "s04320w06840.tif", "s04320w06870.tif", "s04320w06900.tif", "s04320w06930.tif", "s04320w06960.tif", "s04320w06990.tif", "s04320w07020.tif", "s04320w07050.tif", "s04320w07080.tif", "s04320w07110.tif")
#$reprocessTiles | ForEach-Object {
#	$geoPackageFileName = [System.IO.Path]::GetFileNameWithoutExtension($_) + ".gpkg"
#	Get-Treetops -Dsm  ([System.IO.Path]::Combine("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\$_")) -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM\$_" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM\$geoPackageFileName")) -Verbose
#}

#Get-Treetops -ChmMaxima -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\s03450w06540.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM\s03450w06540.tif" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops CHM\s03450w06540.gpkg")) -Verbose
#Get-Treetops -ChmMaxima -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\*.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops CHM")) -Verbose
