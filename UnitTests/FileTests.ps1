$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net9.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net9.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native')

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))


## get sizes of directories below specified path
Get-TreeSize -Path $buildDirectory -Spreadsheet ([System.IO.Path]::Combine((Get-Location), "..\TestResults\build tree size.xlsx")) -Verbose

# .csv generation via PowerShell is also an option
$directorySizes = Get-TreeSize -Path $buildDirectory
$directorySizes | Export-Csv -Path ([System.IO.Path]::Combine((Get-Location), "..\TestResults\build tree size.csv")) -Verbose

## list all files under the specified path
Get-Files -Path $buildDirectory -Spreadsheet ([System.IO.Path]::Combine((Get-Location), "..\TestResults\build files.xlsx")) -Verbose

# .csv generation via PowerShell is also an option
# Not recommended for more than a few files as PowerShell 7.5 is only able to write ~360 files/s to .csv (on a 9950X) and provides no progress.
$files = Get-Files -Path $buildDirectory -Verbose
$files | Select-Object @{Name = "path"; Expression = {Resolve-Path -Path $_.FullName -Relative -RelativeBasePath $buildDirectory}},
                       @{Name = "bytes"; Expression = {$_.Length}},
					   @{Name = "creationTime"; Expression = {$_.CreationTime.ToString("yyyy-MM-dd-THH:mm:ss")}},
					   @{Name = "lastAccessTime"; Expression = {$_.LastAccessTime.ToString("yyyy-MM-dd-THH:mm:ss")}},
					   @{Name = "lastWriteTime"; Expression = {$_.LastWriteTime.ToString("yyyy-MM-dd-THH:mm:ss")}} |
  Export-Csv -Path ([System.IO.Path]::Combine((Get-Location), "..\TestResults\build files.csv"))


