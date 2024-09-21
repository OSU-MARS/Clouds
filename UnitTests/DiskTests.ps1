# This script illustrates cmdlets for diskspd.exe log translation and drive thermal stress. Paths need to be changed to files available for the area of interest.
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net8.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net8.0"))

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

## convert DiskSpd XML results files to longform data
$resultPath = [System.IO.Path]::Combine($env:USERPROFILE, "PhD\tools\marmot\DiskSpd logs")
Convert-DiskSpd -Result $resultPath -Longform ([System.IO.Path]::Combine($resultPath, "DiskSpd.xlsx"))

## read load generation
$sourcePath = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County"
Read-Files -Threads 8 -Input ("$sourcePath\tiles RGB+NIR", "$sourcePath\tiles surrounding distance 1", "$sourcePath\DSM v3 beta", "$sourcePath\orthoimage v3") -Verbose