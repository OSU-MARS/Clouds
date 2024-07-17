$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net8.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net8.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native')

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))

## get sizes of directories below specified path
$directorySizes = Get-TreeSize -Path $buildDirectory
$directorySizes | Export-Csv -Path ([System.IO.Path]::Combine((Get-Location), "..\TestResults\filesystem tree size.csv"))