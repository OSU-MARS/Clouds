$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net8.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net8.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native')

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))
$dataPath = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County"

# DSM ring-based treetop identification
$tile = "s04230w06810"
Get-Treetops -Method DsmRing -Dsm "$dataPath\DSM with outlier rejection\$tile.tif" -Dtm "$dataPath\DTM\$tile.tif" -Diagnostics "$dataPath\DSM with outlier rejection\ring diagnostics" -Treetops "$dataPath\DSM with outlier rejection\$tile.gpkg" -Verbose

# DSM radius-based treetop identification
#$tile = "s04020w06690"
#Get-Treetops -Dsm "$dataPath\DSM\$tile.tif" -Dtm "$dataPath\DTM\$tile.tif" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM\$tile DSM radius.gpkg")) -Verbose

# CHM radius-based treetop identification
#Get-Treetops -Method ChmRadius -Dsm "$dataPath\DSM\s03450w06540.tif" -Dtm "$dataPath\DTM\s03450w06540.tif" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops CHM\s03450w06540.gpkg")) -Verbose
#Get-Treetops -Method ChmRadius -Dsm "$dataPath\DSM\*.tif" -Dtm "$dataPath\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops CHM")) -Verbose

# treetop identification across many tiles
Get-Treetops -Method DsmRing -Dsm "D:\Elliott\GIS\DOGAMI\2009 OLC South Coast\DSM\*.tif" -Dtm "$dataPath\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2009 OLC South Coast\treetops DSM ring")) -Verbose
#Get-Treetops -Method DsmRing -Dsm "$dataPath\DSM\*.tif" -Dtm "$dataPath\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM ring")) -Verbose

#Get-Treetops -Method DsmRadius -Dsm "$dataPath\DSM\*.tif" -Dtm "$dataPath\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM")) -Verbose
#Get-Treetops -Method ChmRadius -Dsm "$dataPath\DSM\*.tif" -Dtm "$dataPath\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops CHM")) -Verbose

# merge treetop and taxa classification tiles into a single treetop file with class prevalence counts within each tree's nominal radius
#Merge-Treetops -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM ring")) -Classification "$dataPath\species 10 m non-normalized" -Verbose

# single threaded recalc of selected tiles
#$tiles = ("s03450w06540", "s03480w06570", "s03480w06690", "s03570w06900", "s03570w06960", "s03570w06990", "s03600w07140", "s03630w07170", "s03660w07200", "s03720w06360", "s03930w07260", "s04050w07260", "s04080w07230", "s04200w07170", "s04230w07170", "s04290w07140", "s04320w07110")
#foreach ($tile in $tiles)
#{
#  Get-Treetops -Method DsmRing -Dsm "$dataPath\DSM\$tile.tif" -Dtm "$dataPath\DTM\$tile.tif" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM ring\$tile.gpkg")) -Verbose
#}
