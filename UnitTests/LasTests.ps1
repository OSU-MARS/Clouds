# This script illustrates cmdlet use. Paths need to be changed to files available for the area of interest.
$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Debug\net8.0"))
#$buildDirectory = ([System.IO.Path]::Combine((Get-Location), "bin\Release\net8.0"))
$env:PATH = $env:PATH + (';' + $buildDirectory + '\runtimes\win-x64\native') # for GDAL

Import-Module -Name ([System.IO.Path]::Combine($buildDirectory, "Clouds.dll"))


## DSM generation, single tile
$tile = "s04200w06810" # "s04230w06810" # "s04020w07050"
Get-Dsm -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles RGB+NIR\$tile.las" -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM testing\$tile.tif" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM" -Snap -Verbose

# DSM generation, 3x3 group of tiles
$tiles = "s042?0w068?0"
Get-Dsm -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles RGB+NIR\$tiles.las" -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM testing" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM" -Snap -Verbose

# DSM generation, all on forest and buffer tiles from 3.5 drive
Get-Dsm -Las ("E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles RGB+NIR", "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles surrounding distance 1") -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3 beta" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM" -Snap -Verbose

## orthoimage generation, single tile
$tile = "s03480w06540" # "s04200w06810" # "s04230w06810" # "s04020w07050"
Get-Orthoimages -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles RGB+NIR\$tile.las" -Image "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\orthoimage v2 16\$tile.tif" -Snap -Verbose

# orthoimage generation, all tiles
Get-Orthoimages -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles RGB+NIR\*.las" -Image "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\orthoimage v2" -Verbose

## local maxima in DSM, 3x3 group of tiles
$tiles = "s042?0w068?0"
Get-LocalMaxima -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM testing\$tiles.tif" -LocalMaxima "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM testing\local maxima" -Verbose

# local maxima in DSM, all tiles
Get-LocalMaxima -Dsm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3 beta" -LocalMaxima "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM v3 beta\local maxima" -Verbose


## high resolution grid metrics bootstrapping from DSM (or CHM or DTM) grid
# Just under 4 Mcells => 600 MB float32 .tif @ 57 standard metrics bands.
$tile = "s04200w06810"
$dsmMetrics = Get-GridMetrics -Cells "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DSM\$tile.tif" -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles RGB+NIR\$tile.las" -Verbose
$dsmMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\$tile DSM resolution.tif", compress: $false)


## grid metrics for area based approaches
$gridPath = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\Elliott ABA grid 10 m EPSG 6557.tif"
$gridMetrics = Get-GridMetrics -Cells $gridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles RGB+NIR\s03660w06750.las" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM\s036?0w067?0.tif" -Verbose
$gridMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\s03660w06750 10 m non-normaized.tif", compress: $false);

# full grid metrics run example: 663 LiDAR tiles (1.92 TB of .las files), 1.03 M ABA cells -> 190 MB raster @ 20 m resolution and 56 bands
$gridPath = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\Elliott ABA grid 10 m EPSG 6557.tif"
$gridMetrics = Get-GridMetrics -Cells $gridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles RGB+NIR\*.las" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM" -Verbose
$gridMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\grid metrics 10 m non-normalized v2.tif", compress: $false);


## scan metrics, single tile
$gridPath = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\Elliott ABA grid 10 m EPSG 6557.tif"
$tile = "s04200w06810"
$scanMetrics = Get-ScanMetrics -Cells $gridPath -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles RGB+NIR\$tile.las" -Verbose
$scanMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\scan metrics $tile 10 m.tif", $false)

# scan metrics, all tiles
$gridPath = "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\Elliott ABA grid 10 m EPSG 6557.tif"
$scanMetrics = Get-ScanMetrics -Cells $gridPath -Las ("E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles RGB+NIR", "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles surrounding distance 1", "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles surrounding distance 2+") -Verbose
$scanMetrics.Write("D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\metrics\scan metrics 10 m.tif", $false)

# read headers from .las or .laz files with basic query
$singleLasFile = Get-LasInfo -Las ([System.IO.Path]::Combine((Get-Location), "PSME LAS 1.4 point type 6.las"))
$lasTiles = Get-LasInfo -Las "E:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles RGB+NIR"

$pointCounts = [long[]](($lasTiles | ForEach-Object {$_}).Header.NumberOfPointRecords)
$megaPoints = [double[]]$pointCounts
for($index = 0; $index -lt $megaPoints.Length; ++$index)
{
    $megaPoints[$index] = $megaPoints[$index] / 1E6
}
$megaPoints | Measure -AllStats


## outlier detection
Repair-NoisePoints -Las "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\tiles testing\s03780w06390.las" -Dtm "D:\Elliott\GIS\DOGAMI\2021 OLC Coos County\DTM"

## georeferencing
# cloud registration
$scanDir = [System.IO.Path]::Combine($env:USERPROFILE, "PhD\data\McDonald-Dunn\Stand 50603")
Register-Cloud -Lat 44.64663 -Long -123.27204 -Z 428 -HorizontalEpsg 6556 -FallbackDate ([System.DateOnly]::new(2024, 5, 11)) -Las (([System.IO.Path]::Combine($scanDir, "scan 1\scan 1 RGB+class.las")), ([System.IO.Path]::Combine($scanDir, "scan 2\scan 2 RGB+class.las")), ([System.IO.Path]::Combine($scanDir, "scan 3\scan 3 RGB+class.las")), ([System.IO.Path]::Combine($scanDir, "scan 4\scan 4 RGB+class.las")))
Register-Cloud -Lat 44.64800 -Long -123.27089 -Z 428 -HorizontalEpsg 6556 -FallbackDate ([System.DateOnly]::new(2024, 5, 11)) -SourceID 5 -Las (([System.IO.Path]::Combine($scanDir, "scan 5\scan 5 RGB+class.las")), ([System.IO.Path]::Combine($scanDir, "scan 6\scan 6 RGB+class.las")))

# cloud reprojection (EPSG:2994, English units, to 6556, metric, in this case)
$lazPath = [System.IO.Path]::Combine($env:USERPROFILE, "PhD\data\McDonald-Dunn\LDQ-44123F3 Airlie South\2012_OLC_Central Coast\pointz\44123F3419.laz")
Convert-CloudCrs -Las $lazPath -HorizontalEpsg 6556