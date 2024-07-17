# Examples of scripting supporting tasks in PowerShell

## build pyramids for virtual raster tiles
# Rather than using GDAL's full power of two ladder, levels 4 and 8 are generated.
# Pyramids are built externally (-ro).
$gdalPath = "C:\Program Files\QGIS 3.34.6\bin"
$gdalAddo = [System.IO.Path]::Combine($gdalPath, "gdaladdo.exe")

$vrtFiles = Get-ChildItem "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\orthoimage v3\*.tif"
foreach ($vrtFile in $vrtFiles)
{
	& $gdalAddo -r average -ro $vrtFile.FullName 4 8
}