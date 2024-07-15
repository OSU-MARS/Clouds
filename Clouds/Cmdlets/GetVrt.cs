using Mars.Clouds.Cmdlets.Hardware;
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
        [ValidateNotNullOrEmpty]
        public List<string> Bands { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path to output .vrt file.")]
        [ValidateNotNullOrEmpty]
        public string Vrt { get; set; }

        [Parameter(HelpMessage = "Minimum fraction of tiles to load for computing approximate virtual raster band statistics, default is 0.333 (every third tile). Bands in the first tile are always sampled for fractions greater than zero.")]
        [ValidateRange(0.0F, 1.0F)]
        public float MinSamplingFraction { get; set; }

        [Parameter(HelpMessage = "Minimum number of tiles to load for computing virtual raster band statistics. Default is 10 to ensure adequate sampling of small virtual rasters, which results in exact statistics (sampling fraction = 1.0) for 10 or fewer tiles and increases sampling above the minimum (-MinSamplingFraction) for up to 30 tiles.")]
        [ValidateRange(0, Int32.MaxValue)]
        public int MinTilesSampled { get; set; }

        [Parameter(HelpMessage = "If set, a .xlsx is created alongside the .vrt with band statistics for each tile (overall statistics for each band are included in the .vrt).")]
        public SwitchParameter Stats { get; set; }

        [Parameter(HelpMessage = "Number of threads, out of -MaxThreads, to use for reading tiles. Default is automatic estimation, which will typically choose single read thread.")]
        [ValidateRange(1, 32)] // arbitrary upper bound
        public int ReadThreads { get; set; }

        [Parameter(HelpMessage = "Frequency at which the status of virtual raster loading and .vrt generation is updated. Default is one second.")]
        public TimeSpan ProgressInterval { get; set; }

        [Parameter(HelpMessage = "Options for subdirectories and files under the specified path. Default is a 16 kB buffer and to ignore inaccessible and directories as otherwise the UnauthorizedAccessException raised blocks enumeration of all other files.")]
        public EnumerationOptions EnumerationOptions { get; set; }

        public GetVrt()
        {
            this.Bands = [];
            this.EnumerationOptions = new()
            {
                BufferSize = 16 * 1024,
                IgnoreInaccessible = true
            };
            // leave this.MaxThreads at default
            this.MinSamplingFraction = 1.0F / 3.0F;
            this.MinTilesSampled = 10;
            this.ProgressInterval = TimeSpan.FromSeconds(1.0);
            this.ReadThreads = -1;
            this.TilePaths = [];
            this.Stats = false;
            this.Vrt = String.Empty;
        }

        private VirtualRasterBandsAndStatistics AssembleVrts()
        {
            Debug.Assert(this.TilePaths.Count > 0);

            // find all tiles
            VirtualRasterBandsAndStatistics vrtBandsAndStats = new(this.TilePaths.Count);
            List<RasterBandStatistics>[][] tileBandStatisticsByVrtUngriddedTileIndex = new List<RasterBandStatistics>[this.TilePaths.Count][];
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
                tileBandStatisticsByVrtUngriddedTileIndex[vrtIndex] = new List<RasterBandStatistics>[tilePaths.Count];
                tilePathsByVrtIndex[vrtIndex] = tilePaths;
                vrtBandsAndStats.Vrts[vrtIndex] = [];
            }

            // read tiles: get metadata for virtual raster position, read data and calculate band statistics if sampled
            int readThreads = this.ReadThreads;
            if (readThreads == -1)
            {
                readThreads = Int32.Min(HardwareCapabilities.Current.GetPracticalReadThreadCount(this.TilePaths, 1.0F, 6.8F), this.MaxThreads);
            }
            int tileMetadataReadsCompleted = 0;
            int tileReadsInitiated = -1;
            int vrtsRead = 0;
            ParallelTasks readVrts = new(Int32.Min(readThreads, tileCountAcrossAllVrts), () =>
            {
                SortedList<DataType, Array?> bandBuffersByDataType = []; // pool buffers for bands to avoid overloading the GC
                TileEnumerator tileEnumerator = new(tilePathsByVrtIndex, this.MinTilesSampled, this.MinSamplingFraction);
                for (int tileReadIndex = Interlocked.Increment(ref tileReadsInitiated); tileReadIndex < tileCountAcrossAllVrts; tileReadIndex = Interlocked.Increment(ref tileReadsInitiated))
                {
                    // find tile matching this index
                    if (tileEnumerator.TryMoveTo(tileReadIndex) == false)
                    {
                        continue; // shouldn't be reachable
                    }

                    // must be newed distinctly since added to vrt
                    // Defer data read so band data buffers can be object pooled to avoid overloading the GC.
                    using Dataset tileDataset = Gdal.Open(tileEnumerator.Current, Access.GA_ReadOnly);
                    Raster tile = Raster.Read(tileDataset, readData: false);

                    int vrtIndex = tileEnumerator.VrtIndex;
                    VirtualRaster<Raster> vrt = vrtBandsAndStats.Vrts[vrtIndex];
                    
                    int ungriddedTileIndexInVrt;
                    lock (vrt)
                    {
                        ungriddedTileIndexInVrt = vrt.TileCount;
                        vrt.Add(tile);

                        ++tileMetadataReadsCompleted;
                        vrtsRead = Int32.Max(vrtsRead, tileEnumerator.VrtIndex);
                    }

                    if (tileEnumerator.SampleTile)
                    {
                        List<RasterBandStatistics> tileStatistics = [];
                        foreach (RasterBand band in tile.GetBands())
                        {
                            string bandName = band.Name;
                            DataType bandDataType = band.GetGdalDataType();
                            bool bandDataPreviouslyRead = band.HasData;
                            if (bandDataPreviouslyRead == false)
                            {
                                bandBuffersByDataType.TryGetValue(bandDataType, out Array? bandDataBuffer);
                                bool didOwn = band.TryTakeOwnershipOfDataBuffer(bandDataBuffer);
                                band.ReadDataInSameCrsAndTransform(tileDataset); // band is reading its own data so CRS and transform are guaranteed
                            }
                            tileStatistics.Add(band.GetStatistics());
                            if (bandDataPreviouslyRead == false)
                            {
                                Array bandDataBuffer = band.ReleaseData();
                                bandBuffersByDataType[bandDataType] = bandDataBuffer;
                            }
                        }

                        // VirtualRaster.CreateTileGrid() returns tile xy indices in the order in which tiles were added to the raster
                        // Tile statistics needed to be placed in the same order for transfer to the statistics grid.
                        tileBandStatisticsByVrtUngriddedTileIndex[vrtIndex][ungriddedTileIndexInVrt] = tileStatistics;
                    }
                }
            }, new());

            VirtualRaster<Raster>[] vrts = vrtBandsAndStats.Vrts;
            TimedProgressRecord progress = new("Get-Vrt", "0 tiles read from " + (vrts.Length == 1 ? "virtual raster..." : vrtsRead + " of " + vrts.Length + " virtual rasters (" + readVrts.Count + (readVrts.Count == 1 ? " thread, " : " threads, ") + readVrts.Count + " reading)..."));
            this.WriteProgress(progress);
            while (readVrts.WaitAll(this.ProgressInterval) == false)
            {
                progress.StatusDescription = tileMetadataReadsCompleted + " tiles read from " + (vrts.Length == 1 ? "virtual raster..." : vrtsRead + " of " + vrts.Length + " virtual rasters (" + readVrts.Count + (readVrts.Count == 1 ? " thread, " : " threads, ") + readVrts.Count + " reading)...");
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

                GridNullable<List<RasterBandStatistics>?>? vrtStats = null;
                if (this.MinSamplingFraction > 0.0F)
                {
                    vrtStats = new(vrt.Crs, vrt.TileTransform, vrt.VirtualRasterSizeInTilesX, vrt.VirtualRasterSizeInTilesY);
                    List<RasterBandStatistics>?[] tileBandStatisticsByUngriddedTileIndexInVrt = tileBandStatisticsByVrtUngriddedTileIndex[vrtIndex];
                    Debug.Assert(tileBandStatisticsByUngriddedTileIndexInVrt.Length == vrt.TileCount);

                    for (int ungriddedTileIndex = 0; ungriddedTileIndex < tileBandStatisticsByUngriddedTileIndexInVrt.Length; ++ungriddedTileIndex)
                    {
                        List<RasterBandStatistics>? bandStatisticsForTile = tileBandStatisticsByUngriddedTileIndexInVrt[ungriddedTileIndex];
                        if (bandStatisticsForTile != null) // not all tiles are sampled for statistics
                        {
                            Debug.Assert(vrtStats[tileIndexX[ungriddedTileIndex], tileIndexY[ungriddedTileIndex]] == null);
                            vrtStats[tileIndexX[ungriddedTileIndex], tileIndexY[ungriddedTileIndex]] = bandStatisticsForTile;
                        }
                    }
                }

                vrtBandsAndStats.TileBandStatisticsByVrtIndex[vrtIndex] = vrtStats;
            }

            vrtBandsAndStats.VrtAssemblyTime = progress.Stopwatch.Elapsed;
            return vrtBandsAndStats;
        }

        private TileStatisticsTable CreateTileStatisticsTable(VirtualRasterBandsAndStatistics vrtBandsAndStats)
        {
            TileStatisticsTable statsTable = new();
            for (int vrtIndex = 0; vrtIndex < vrtBandsAndStats.Vrts.Length; ++vrtIndex)
            {
                GridNullable<List<RasterBandStatistics>?>? statsGridForVrt = vrtBandsAndStats.TileBandStatisticsByVrtIndex[vrtIndex];
                if (statsGridForVrt == null)
                {
                    continue;
                }

                VirtualRaster<Raster> vrt = vrtBandsAndStats.Vrts[vrtIndex];
                for (int tileIndexY = 0; tileIndexY < statsGridForVrt.SizeY; ++tileIndexY)
                {
                    for (int tileIndexX = 0; tileIndexX < statsGridForVrt.SizeX; ++tileIndexX)
                    {
                        List<RasterBandStatistics>? tileBandStatistics = statsGridForVrt[tileIndexX, tileIndexY];
                        if (tileBandStatistics == null)
                        {
                            continue;
                        }

                        Raster? tile = vrt[tileIndexX, tileIndexY];
                        if (tile == null)
                        {
                            throw new InvalidOperationException("Statistics are present for virtual raster '" + this.TilePaths[vrtIndex] + "' tile at (" + tileIndexX + ", " + tileIndexY + ") but no tile is present at the same location in the virtual raster.");
                        }

                        IEnumerator<RasterBand> tileBands = tile.GetBands().GetEnumerator();
                        string tileName = Tile.GetName(tile.FilePath);
                        for (int bandIndex = 0; bandIndex < tileBandStatistics.Count; ++bandIndex)
                        {
                            if (tileBands.MoveNext() == false)
                            {
                                throw new InvalidOperationException("Could not move to band number " + (bandIndex + 1) + " in tile '" + tile.FilePath + "'."); // report GDAL band number
                            }

                            string bandName = tileBands.Current.Name;
                            RasterBandStatistics bandStatistics = tileBandStatistics[bandIndex];
                            statsTable.Add(tileName, bandName, bandStatistics);
                        }
                    }
                }
            }

            return statsTable;
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

            // create and write .vrt
            // Incremental progress is not shown for dataset generation as time spent is negligible at 2000+ tiles. Runtime is dominated
            // by getting tile metadata through GDAL and estimating band statistics.
            ProgressRecord progress = new(0, "Get-Vrt", "Creating and writing .vrt...");
            this.WriteProgress(progress);

            VirtualRaster<Raster>[] vrts = vrtBandsAndStats.Vrts;
            VrtDataset? vrtDataset = null;
            for (int vrtIndex = 0; vrtIndex < vrts.Length; ++vrtIndex)
            {
                // filter virtual raster bands if 
                VirtualRaster<Raster> vrt = vrtBandsAndStats.Vrts[vrtIndex];
                List<string> virtualRasterBandNames = vrt.BandNames;
                if (this.Bands.Count > 0)
                {
                    virtualRasterBandNames = [];
                    for (int vrtBandIndex = 0; vrtBandIndex < vrt.BandNames.Count; ++vrtBandIndex)
                    {
                        string vrtBandName = vrt.BandNames[vrtBandIndex];
                        if (this.Bands.Contains(vrtBandName))
                        {
                            virtualRasterBandNames.Add(vrtBandName);
                        }
                    }
                }

                vrtDataset ??= vrts[0].CreateDataset();
                vrtDataset.AppendBands(vrtDatasetDirectory, vrts[vrtIndex], virtualRasterBandNames, vrtBandsAndStats.TileBandStatisticsByVrtIndex[vrtIndex]);
            }

            if (vrtDataset != null)
            {
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
            }
            else
            {
                this.WriteWarning("No virtual raster was created. If -" + nameof(this.Bands) + " was specified does it include at least one band that's present in an input tile?");
            }

            this.WriteVerbose("Assembled " + vrtBandsAndStats.GetTileCount() + " tiles into " + vrtBandsAndStats.Vrts.Length + " virtual rasters in " + vrtBandsAndStats.VrtAssemblyTime.ToElapsedString() + ".");
            base.ProcessRecord();
        }

        private class VirtualRasterBandsAndStatistics
        {
            public GridNullable<List<RasterBandStatistics>?>?[] TileBandStatisticsByVrtIndex { get; private init; }
            public TimeSpan VrtAssemblyTime { get; set; }
            public VirtualRaster<Raster>[] Vrts { get; private init; }

            public VirtualRasterBandsAndStatistics(int capacity)
            {
                this.TileBandStatisticsByVrtIndex = new GridNullable<List<RasterBandStatistics>?>?[capacity];
                this.Vrts = new VirtualRaster<Raster>[capacity];
            }

            public int GetTileCount()
            {
                int tiles = 0;
                for (int vrtIndex = 0; vrtIndex < this.Vrts.Length; ++vrtIndex)
                {
                    tiles += this.Vrts[vrtIndex].TileCount;
                }

                return tiles;
            }
        }
    }
}
