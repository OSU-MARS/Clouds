$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net7.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net7.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native')

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

# DSM ring-based treetop identification
#Get-Treetops -Method DsmRing -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\s03840w07050.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM\s03840w07050.tif" -Diagnostics ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\ring diagnostics")) -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM ring\s03840w07050 new.gpkg")) -Verbose

# DSM radius-based treetop identification
#Get-Treetops -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\s04020w06690.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM\s04020w06690.tif" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM\s04020w06690 new.gpkg")) -Verbose

# CHM radius-based treetop identification
#Get-Treetops -Method ChmRadius -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\s03450w06540.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM\s03450w06540.tif" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops CHM\s03450w06540.gpkg")) -Verbose
#Get-Treetops -Method ChmRadius -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\*.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops CHM")) -Verbose

# treetop identification across many tiles
#Get-Treetops -Method ChmRadius -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\*.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops CHM")) -Verbose
#Get-Treetops -Method DsmRadius -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\*.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM")) -Verbose
#Get-Treetops -Method DsmRing -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\*.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM ring")) -Verbose

# merge treetop and taxa classification tiles into a single treetop file with class prevalence counts within each tree's nominal radius
#Merge-Treetops -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM ring")) -Classification "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\species 10 m non-normalized" -Verbose

# single threaded recalc of selected tiles
#$tileNames = ("s03450w06540", "s03480w06570", "s03480w06690", "s03570w06900", "s03570w06960", "s03570w06990", "s03600w07140", "s03630w07170", "s03660w07200", "s03720w06360", "s03930w07260", "s04050w07260", "s04080w07230", "s04200w07170", "s04230w07170", "s04290w07140", "s04320w07110")
#foreach ($tileName in $tileNames)
#{
#  Get-Treetops -Method DsmRing -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\$tileName.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM\$tileName.tif" -Treetops ([System.IO.Path]::Combine($env:USERPROFILE, "PhD\Elliott\GIS\DOGAMI\2021 OLC Coos County\treetops DSM ring\$tileName.gpkg")) -Verbose
#}
