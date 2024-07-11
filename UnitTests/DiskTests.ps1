# read diskspd.exe logs
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net8.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net8.0"))

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

$resultPath = [System.IO.Path]::Combine($env:USERPROFILE, "PhD\\tools\\marmot\DiskSpd logs")
Convert-DiskSpd -Result $resultPath -Longform ([System.IO.Path]::Combine($resultPath, "DiskSpd.xlsx"))