using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Vrt;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.Intrinsics.X86;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Vrt")]
    public class GetVrt : GdalCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "List of directory paths, including wildcards, to search for virtual raster tiles. Each directory is assumed to contain a distinct set of tiles.")]
        [ValidateNotNullOrEmpty]
        public List<string> TilePaths { get; set; }

        [Parameter(HelpMessage = "List of bands to include in the virtual raster. All available bands are included if not specified.")]
        public List<string> Bands { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path to output .vrt file.")]
        public string Vrt { get; set; }

        [Parameter(HelpMessage = "Options for subdirectories and files under the specified path. Default is a 16 kB buffer and to ignore inaccessible and directories as otherwise the UnauthorizedAccessException raised blocks enumeration of all other files.")]
        public EnumerationOptions EnumerationOptions { get; set; }

        [Parameter(HelpMessage = "Minimum fraction of tiles to load for computing approximate virtual raster band statistics, default is 0.333 (every third tile). Bands in the first tile are always sampled for fractions greater than zero.")]
        [ValidateRange(0.0F, 1.0F)]
        public float MinSamplingFraction { get; set; }

        [Parameter(HelpMessage = "Minimum number of tiles to load for computing virtual raster band statistics. Default is 10 to ensure adequate sampling of small virtual rasters, which results in exact statistics (sampling fraction = 1.0) for 10 or fewer tiles and increases sampling above the minimum (-MinSamplingFraction) for up to 30 tiles.")]
        [ValidateRange(0, Int32.MaxValue)]
        public int MinTilesSampled { get; set; }

        [Parameter(HelpMessage = "Frequency at which the status of virtual raster loading and .vrt generation is updated. Default is one second.")]
        public TimeSpan ProgressInterval { get; set; }

        [Parameter(HelpMessage = "If set, a .xlsx is created alongside the .vrt with band statistics for each tile (overall statistics for each band are included in the .vrt).")]
        public SwitchParameter Stats { get; set; }

        public GetVrt()
        {
            this.Bands = [];
            this.EnumerationOptions = new()
            {
                BufferSize = 16 * 1024,
                IgnoreInaccessible = true
            };
            this.ProgressInterval = TimeSpan.FromSeconds(1.0);
            this.MinSamplingFraction = 1.0F / 3.0F;
            this.MinTilesSampled = 10;
            this.TilePaths = [];
            this.Stats = false;
            this.Vrt = String.Empty;
        }

        private VirtualRasterBandsAndStatistics AssembleVrts()
        {
            Debug.Assert(this.TilePaths.Count > 0);

            // find all tiles
            VirtualRasterBandsAndStatistics vrtBandsAndStats = new(this.TilePaths.Count);
            RasterBandStatistics[][][] statisticsByUngriddedTileIndex = new RasterBandStatistics[this.TilePaths.Count][][];
            List<string>[] tilePathsByVrtIndex = new List<string>[this.TilePaths.Count];
            int tileCountAcrossAllVrts = 0;
            for (int vrtIndex = 0; vrtIndex < this.TilePaths.Count; ++vrtIndex)
            {
                string vrtPath = this.TilePaths[vrtIndex];
                bool vrtPathIsDirectory = Directory.Exists(vrtPath);
                string? vrtDirectoryPath = vrtPathIsDirectory ? vrtPath : Path.GetDirectoryName(vrtPath);
                if (vrtDirectoryPath == null)
                {
                    throw new ArgumentOutOfRangeException(nameof(vrtPath), "Virtual raster path '" + vrtPath + "' does not contain a directory.");
                }
                if (Directory.Exists(vrtDirectoryPath) == false)
                {
                    throw new ArgumentOutOfRangeException(nameof(vrtPath), "Directory indicated by virtual raster path '" + vrtPath + "' does not exist.");
                }

                string? vrtSearchPattern = vrtPathIsDirectory ? "*" + Constant.File.GeoTiffExtension : Path.GetFileName(vrtPath);
                if (vrtSearchPattern == null)
                {
                    throw new ParameterOutOfRangeException(nameof(this.TilePaths), "Tile path '" + vrtPath + "' is not a directory but also does not appear to contain a search pattern (or specify an individual file).");
                }

                List<string> tilePaths = Directory.EnumerateFiles(vrtDirectoryPath, vrtSearchPattern, this.EnumerationOptions).ToList();
                if (tilePaths.Count < 1)
                {
                    throw new ParameterOutOfRangeException(nameof(this.TilePaths), "-" + nameof(this.TilePaths) + " does not specify any virtual raster tiles.");
                }

                tileCountAcrossAllVrts += tilePaths.Count;
                statisticsByUngriddedTileIndex[vrtIndex] = new RasterBandStatistics[tilePaths.Count][];
                tilePathsByVrtIndex[vrtIndex] = tilePaths;
                vrtBandsAndStats.Vrts[vrtIndex] = [];
            }

            // read tiles: get metadata for virtual raster position, read data and calculate band statistics if sampled
            int readThreads = Int32.Min(tileCountAcrossAllVrts, this.MaxThreads);
            int tileMetadataReadsCompleted = 0;
            int tileReadsInitiated = -1;
            int vrtsRead = 0;
            ParallelTasks readVrts = new(readThreads, () =>
            {
                TileEnumerator tileEnumerator = new(tilePathsByVrtIndex, this.MinTilesSampled, this.MinSamplingFraction);
                for (int tileReadIndex = Interlocked.Increment(ref tileReadsInitiated); tileReadIndex < tileCountAcrossAllVrts; tileReadIndex = Interlocked.Increment(ref tileReadsInitiated))
                {
                    // find tile matching this index
                    if (tileEnumerator.TryMoveTo(tileReadIndex) == false)
                    {
                        continue; // shouldn't be reachable
                    }

                    using Dataset tileDataset = Gdal.Open(tileEnumerator.Current, Access.GA_ReadOnly);
                    Raster tile = Raster.Read(tileDataset, readData: false);

                    int vrtIndex = tileEnumerator.VrtIndex;
                    VirtualRaster<Raster> vrt = vrtBandsAndStats.Vrts[vrtIndex];

                    List<string> bandsToSampleInVrt;
                    int ungriddedTileIndexInVrt;
                    lock (vrt)
                    {
                        ungriddedTileIndexInVrt = vrt.TileCount;
                        vrt.Add(tile);

                        // intersection between -Bands and virtual raster has to be found somewhere
                        // Intersection can't occur until a tile's been added to the virtual raster so that it knows what its bands
                        // are. Intersection also needs to be thread safe, which could be done by each worker writing an intersection
                        // for every virtual raster it hits the array of band names. Since a lock has to be taken to add a tile to a
                        // virtual raster and GetBandsInVrt() modifies only the passed virtual raster index it's slightly cleaner to
                        // intersect once within the lock.
                        bandsToSampleInVrt = this.GetBandsInVrt(vrtBandsAndStats, vrtIndex);

                        ++tileMetadataReadsCompleted;
                        vrtsRead = Int32.Max(vrtsRead, tileEnumerator.VrtIndex);
                    }

                    if (tileEnumerator.SampleTile)
                    {
                        RasterBandStatistics[] tileStatistics = new RasterBandStatistics[bandsToSampleInVrt.Count];
                        for (int bandIndex = 0; bandIndex < bandsToSampleInVrt.Count; ++bandIndex)
                        {
                            string bandName = bandsToSampleInVrt[bandIndex];
                            RasterBand band = tile.GetBand(bandName);
                            bool bandDataPreviouslyRead = band.HasData;
                            if (bandDataPreviouslyRead == false)
                            {
                                band.ReadData(tileDataset);
                            }
                            tileStatistics[bandIndex] = band.GetStatistics();
                            if (bandDataPreviouslyRead == false)
                            {
                                band.ReleaseData();
                            }

                        }

                        // VirtualRaster.CreateTileGrid() returns tile xy indices in the order in which tiles were added to the raster
                        // Tile statistics needed to be placed in the same order for transfer to the statistics grid.
                        statisticsByUngriddedTileIndex[vrtIndex][ungriddedTileIndexInVrt] = tileStatistics;
                    }
                }
            });

            VirtualRaster<Raster>[] vrts = vrtBandsAndStats.Vrts;
            TimedProgressRecord progress = new("Get-Vrt", "0 tiles read from " + (vrts.Length == 1 ? "virtual raster..." : vrtsRead + " of " + vrts.Length + " virtual rasters..."));
            this.WriteProgress(progress);
            while (readVrts.WaitAll(this.ProgressInterval) == false)
            {
                progress.StatusDescription = tileMetadataReadsCompleted + " tiles read from " + (vrts.Length == 1 ? "virtual raster..." : vrtsRead + " of " + vrts.Length + " virtual rasters...");
                progress.Update(tileMetadataReadsCompleted, tileCountAcrossAllVrts); // likely optimistic by band sampling time
                this.WriteProgress(progress);
            }

            // check virtual rasters for consistency with each other
            VirtualRaster<Raster>? previousVrt = null;
            for (int vrtIndex = 0; vrtIndex < vrts.Length; ++vrtIndex)
            {
                VirtualRaster<Raster> vrt = vrts[vrtIndex];
                (int[] tileIndexX, int[] tileIndexY) = vrt.CreateTileGrid();

                if (previousVrt != null)
                {
                    // for now, require that tile sets be exactly matched
                    if (SpatialReferenceExtensions.IsSameCrs(vrt.Crs, previousVrt.Crs) == false)
                    {
                        throw new NotSupportedException("Virtual raster '" + this.TilePaths[vrtIndex - 1] + "' is in '" + previousVrt.Crs.GetName() + "' while '" + this.TilePaths[vrtIndex] + "' is in '" + vrt.Crs.GetName() + "'.");
                    }
                    if (vrt.IsSameExtentAndSpatialResolution(previousVrt) == false)
                    {
                        throw new NotSupportedException("Virtual raster '" + this.TilePaths[vrtIndex - 1] + "' and '" + this.TilePaths[vrtIndex] + "' differ in spatial extent or resolution. Sizes are " + previousVrt.VirtualRasterSizeInTilesX + " by " + previousVrt.VirtualRasterSizeInTilesY + " and " + vrt.VirtualRasterSizeInTilesX + " by " + vrt.VirtualRasterSizeInTilesY + " tiles with tiles being " + previousVrt.TileCellSizeX + " by " + previousVrt.TileSizeInCellsY + " and " + vrt.TileSizeInCellsX + " by " + vrt.TileSizeInCellsY + " cells, respectively.");
                    }
                }

                Debug.Assert(vrtBandsAndStats.BandsByVrtIndex[vrtIndex] != null);

                GridNullable<RasterBandStatistics[]>? vrtStats = null;
                if (this.MinSamplingFraction > 0.0F)
                {
                    vrtStats = new(vrt.Crs, vrt.TileTransform, vrt.VirtualRasterSizeInTilesX, vrt.VirtualRasterSizeInTilesY);
                    RasterBandStatistics[]?[] bandStatisticsForVrt = statisticsByUngriddedTileIndex[vrtIndex];
                    Debug.Assert(bandStatisticsForVrt.Length == vrt.TileCount);

                    for (int ungriddedTileIndex = 0; ungriddedTileIndex < bandStatisticsForVrt.Length; ++ungriddedTileIndex)
                    {
                        RasterBandStatistics[]? bandStatisticsForTile = bandStatisticsForVrt[ungriddedTileIndex];
                        if (bandStatisticsForTile != null) // not all tiles are sampled for statistics
                        {
                            Debug.Assert(vrtStats[tileIndexX[ungriddedTileIndex], tileIndexY[ungriddedTileIndex]] == null);
                            vrtStats[tileIndexX[ungriddedTileIndex], tileIndexY[ungriddedTileIndex]] = bandStatisticsForTile;
                        }
                    }
                }

                vrtBandsAndStats.StatisticsByVrtIndex[vrtIndex] = vrtStats;
            }

            return vrtBandsAndStats;
        }

        // could be merged into AssembleVrts() but it's unclear doing so would be helpful
        private int CheckBands(VirtualRasterBandsAndStatistics vrtBandsAndStats)
        {
            VirtualRaster<Raster>[] vrts = vrtBandsAndStats.Vrts;
            List<string>[] bandsByVrtIndex = vrtBandsAndStats.BandsByVrtIndex;

            if (this.Bands.Count > 0)
            {
                int bandsFound = 0;
                HashSet<string> uniqueBandsFound = [];
                for (int vrtIndex = 0; vrtIndex < vrts.Length; ++vrtIndex)
                {
                    List<string> vrtBands = bandsByVrtIndex[vrtIndex];
                    bandsFound += vrtBands.Count;

                    for (int bandIndex = 0; bandIndex < vrtBands.Count; ++bandIndex)
                    {
                        uniqueBandsFound.Add(vrtBands[bandIndex]);
                    }
                }

                if (uniqueBandsFound.Count != this.Bands.Count)
                {
                    throw new ParameterOutOfRangeException(nameof(this.Bands), "-" + nameof(this.Bands) + " specifies " + this.Bands.Count + " bands but " + bandsFound + " matching bands were found with " + uniqueBandsFound.Count + " unique names. Are the entries in -" + nameof(this.Bands) + " unique?");
                }
            }

            for (int vrtIndex = 0; vrtIndex < vrts.Length; ++vrtIndex)
            {
                if (bandsByVrtIndex[vrtIndex].Count > 0)
                {
                    return vrtIndex;
                }
            }

            throw new ParameterOutOfRangeException(nameof(this.Bands), "Either the virtual raster tiles specified by -" + nameof(this.TilePaths) + " contain no bands or -" + nameof(this.Bands) + " specifies none of the bands present in the tiles.");
        }

        private TileStatisticsTable CreateTileStatisticsTable(VirtualRasterBandsAndStatistics vrtBandsAndStats)
        {
            TileStatisticsTable statsTable = new();
            for (int vrtIndex = 0; vrtIndex < vrtBandsAndStats.Vrts.Length; ++vrtIndex)
            {
                GridNullable<RasterBandStatistics[]>? statsGrid = vrtBandsAndStats.StatisticsByVrtIndex[vrtIndex];
                if (statsGrid == null)
                {
                    continue;
                }

                VirtualRaster<Raster> vrt = vrtBandsAndStats.Vrts[vrtIndex];
                List<string> bandNames = vrtBandsAndStats.BandsByVrtIndex[vrtIndex];
                for (int tileIndexY = 0; tileIndexY < statsGrid.SizeY; ++tileIndexY)
                {
                    for (int tileIndexX = 0; tileIndexX < statsGrid.SizeX; ++tileIndexX)
                    {
                        RasterBandStatistics[]? tileStatistics = statsGrid[tileIndexX, tileIndexY];
                        if (tileStatistics == null)
                        {
                            continue;
                        }

                        Raster? tile = vrt[tileIndexX, tileIndexY];
                        if (tile == null)
                        {
                            throw new InvalidOperationException("Statistics are present for virtual raster '" + this.TilePaths[vrtIndex] + "' tile at (" + tileIndexX + ", " + tileIndexY + ") but no tile is present at the same location in the virtual raster.");
                        }

                        Debug.Assert(tileStatistics.Length == bandNames.Count);

                        string tileName = Tile.GetName(tile.FilePath);
                        for (int bandIndex = 0; bandIndex < tileStatistics.Length; ++bandIndex)
                        {
                            string bandName = bandNames[bandIndex];
                            statsTable.Add(tileName, bandName, tileStatistics[bandIndex]);
                        }
                    }
                }
            }

            return statsTable;
        }

        private List<string> GetBandsInVrt(VirtualRasterBandsAndStatistics vrtBandsAndStats, int vrtIndex)
        {
            List<string>?[] bandsByVrtIndex = vrtBandsAndStats.BandsByVrtIndex;

            List<string>? bandsToSampleInVrt = bandsByVrtIndex[vrtIndex];
            if (bandsToSampleInVrt == null)
            {
                VirtualRaster<Raster> vrt = vrtBandsAndStats.Vrts[vrtIndex];
                if (this.Bands.Count > 0)
                {
                    bandsToSampleInVrt = [];
                    for (int bandIndex = 0; bandIndex < this.Bands.Count; ++bandIndex)
                    {
                        string bandName = this.Bands[bandIndex];
                        if (vrt.BandNames.Contains(bandName, StringComparer.Ordinal))
                        {
                            bandsToSampleInVrt.Add(bandName);
                        }
                    }
                }
                else
                {
                    bandsToSampleInVrt = vrt.BandNames;
                }

                bandsByVrtIndex[vrtIndex] = bandsToSampleInVrt;
            }

            Debug.Assert(bandsToSampleInVrt != null);
            return bandsToSampleInVrt;
        }

        protected override void ProcessRecord()
        {
            string? vrtDatasetDirectory = Path.GetDirectoryName(this.Vrt);
            if (vrtDatasetDirectory == null)
            {
                throw new ParameterOutOfRangeException(nameof(this.Vrt), "-" + nameof(this.Vrt) + " does not contain a directory path.");
            }
            if ((this.MinSamplingFraction > 0.0F) && (Fma.IsSupported == false))
            {
                throw new ParameterOutOfRangeException(nameof(this.MinSamplingFraction), "Estimation of band statistics requires at least AVX2 and, potentially, FMA instructions. -" + nameof(this.MinSamplingFraction) + " = 0.0F can be used to disable statistics and circumvent this restriction on older processors.");
            }

            // read tile metadata, checking inputs for spatial and band data type consistency
            VirtualRasterBandsAndStatistics vrtBandsAndStats = this.AssembleVrts();
            int firstVrtIndex = this.CheckBands(vrtBandsAndStats);

            // create and write .vrt
            // Incremental progress is not shown for dataset generation as time spent is negligible at 2000+ tiles. Runtime is dominated
            // by getting tile metadata through GDAL and estimating band statistics.
            ProgressRecord progress = new(0, "Get-Vrt", "Creating and writing .vrt...");
            this.WriteProgress(progress);

            VirtualRaster<Raster>[] vrts = vrtBandsAndStats.Vrts;
            VrtDataset vrtDataset = vrts[firstVrtIndex].CreateDataset(vrtDatasetDirectory, vrtBandsAndStats.BandsByVrtIndex[firstVrtIndex], vrtBandsAndStats.StatisticsByVrtIndex[firstVrtIndex]);
            for (int vrtIndex = firstVrtIndex + 1; vrtIndex < vrts.Length; ++vrtIndex)
            {
                vrtDataset.AppendBands(vrtDatasetDirectory, vrts[vrtIndex], vrtBandsAndStats.BandsByVrtIndex[vrtIndex], vrtBandsAndStats.StatisticsByVrtIndex[vrtIndex]);
            }

            vrtDataset.WriteXml(this.Vrt);

            if (this.Stats)
            {
                // create and write .xlsx
                progress.StatusDescription = "Creating and writing .xlsx...";
                this.WriteProgress(progress);

                string statsFilePath = PathExtensions.ReplaceExtension(this.Vrt, Constant.File.XlsxExtension);
                TileStatisticsTable statsTable = this.CreateTileStatisticsTable(vrtBandsAndStats);
                using FileStream stream = new(statsFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, Constant.File.DefaultBufferSize); // SpreadsheetDocument.Create() requires read as well as write
                statsTable.Write(stream);
            }

            base.ProcessRecord();
        }

        private class VirtualRasterBandsAndStatistics
        {
            public List<string>[] BandsByVrtIndex { get; private init; }
            public GridNullable<RasterBandStatistics[]>?[] StatisticsByVrtIndex { get; private init; }
            public VirtualRaster<Raster>[] Vrts { get; private init; }

            public VirtualRasterBandsAndStatistics(int capacity)
            {
                this.BandsByVrtIndex = new List<string>[capacity];
                this.StatisticsByVrtIndex = new GridNullable<RasterBandStatistics[]>[capacity];
                this.Vrts = new VirtualRaster<Raster>[capacity];
            }
        }
    }
}
