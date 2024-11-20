using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Reflection.Metadata;
using System.Runtime;
using System.Runtime.Intrinsics.X86;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Dsm")]
    public class GetDsm : LasTilesToTilesCmdlet
    {
        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Path to write DSM to as a GeoTIFF or path to a directory to write DSM tiles to. The DSM's cell sizes and positions will be the same as the DTM's.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dsm { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path to a directory containing DTM tiles whose file names match the point cloud tiles. Each DTM must be a single precision floating point raster with ground surface heights in the same CRS as the point cloud tiles. The read capability of the DTM's storage is assumed to be the same as or greater than the DSM's storage.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dtm { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating mean ground elevations. Default is the first band.")]
        [ValidateNotNullOrWhiteSpace]
        public string? DtmBand { get; set; }

        [Parameter(HelpMessage = "If set, calculate the DSM and canompy maxima models' slope and aspect.")]
        public SwitchParameter SlopeAspect { get; set; }

        [Parameter(HelpMessage = "Separation distance between DSM layer and subsurface layer. Default is no distance specified which leaves DSM subsurface extraction disabled. Specifying a gap distance enables the DSM's subsurface layer and subsurface buffer used to look for gaps.")]
        [ValidateRange(0.0F, 300.0F)] // arbitrary upper bound
        public float SubsurfaceGap { get; set; }

        public GetDsm()
        {
            // leave this.DataThreads at default
            this.Dsm = String.Empty;
            this.Dtm = String.Empty;
            this.DtmBand = null;
            // leave this.MetadataThreads at default
            this.SlopeAspect = false;
            this.SubsurfaceGap = Single.NaN;
        }

        protected override void ProcessRecord()
        {
            if (Fma.IsSupported == false)
            {
                throw new NotSupportedException("Generation of a canopy maxima model requires FMA instructions (and DSM subsurface generation uses AVX).");
            }

            HardwareCapabilities hardwareCapabilities = HardwareCapabilities.Current;
            if (this.DataThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.DataThreads), "-" + nameof(this.DataThreads) + " must be at least two.");
            }

            const string cmdletName = "Get-Dsm";
            bool dsmPathIsDirectory = Directory.Exists(this.Dsm);
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, nameof(this.Dsm), dsmPathIsDirectory);

            VirtualRaster<DigitalSurfaceModel> dsm = new(lasGrid);
            bool dtmPathIsDirectory = Directory.Exists(this.Dtm);
            DsmReadCreateWrite dsmReadCreateWrite = DsmReadCreateWrite.Create(lasGrid, dsm, this.SlopeAspect, dsmPathIsDirectory, dtmPathIsDirectory, this.NoWrite);
            DigitalSurfaceModelBands dsmBands = DigitalSurfaceModelBands.Default;
            if (Single.IsNaN(this.SubsurfaceGap))
            {
                dsmBands |= DigitalSurfaceModelBands.Subsurface;
            }
            if (this.SlopeAspect)
            {
                dsmBands |= DigitalSurfaceModelBands.SlopeAspect;
            }

            // spin up point cloud read and tile worker threads
            // For small sets of tiles, runtime is dominated by DSM creation latency after tiles are read into memory (streaming read
            // would be helpful but isn't implemented) with filling and sorting the workers' z and source ID lists for each cell being
            // the primary component of runtime. Smaller sets therefore benefit from having up to one worker thread per tile and, up to
            // the point cloud density, greater initial list capacities. For larger sets of tiles, worker requirements set by tile read
            // speed.
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetPointsToDsmBandwidth(dsmBands, this.Unbuffered);
            int readThreads = this.GetLasTileReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs, minWorkerThreadsPerReadThread: 0);

            int preferredCompletionThreadsAsymptotic = Int32.Min(this.DataThreads - readThreads, Int32.Max(readThreads / 3, 2)); // provide at least two completion threads as it appears sometimes beneficial to have more than one
            // but with small numbers of tiles the preferred number of workers increases to reduce overall latency
            int preferredCompletionThreadsWithFewTiles = Int32.Min(this.DataThreads, 25 - (int)(1.5F * lasGrid.NonNullCells)); // negative for 17+ tiles
            // default number of workers is the number of tiles or the asymptotic limit with large number of tiles, whichever is less
            // No value in more than one worker per tile.
            int tileCompletionThreads = Int32.Min(lasGrid.NonNullCells, Int32.Max(preferredCompletionThreadsWithFewTiles, preferredCompletionThreadsAsymptotic));
            Debug.Assert(readThreads + tileCompletionThreads <= this.DataThreads);

            using SemaphoreSlim readSemaphore = new(initialCount: readThreads, maxCount: readThreads);
            ParallelTasks dsmTasks = new(readThreads + tileCompletionThreads, () =>
            {
                RasterBand<float>? dtmTile = null;
                byte[]? pointReadBuffer = null;
                float[]? dsmSubsurfaceBuffer = null;
                while (dsmReadCreateWrite.TileWritesInitiated < lasGrid.NonNullCells)
                {
                    // create canopy maxima models and write as many tiles as are available for completion
                    // An emphasis on completion minimizes total memory footprint by keeping as few tiles in DDR as is practical.
                    // TODO: support .vrt creation
                    dsmReadCreateWrite.TryWriteCompletedTiles(this.CancellationTokenSource, bandStatisticsByTile: null);

                    // if all available tiles are completed and tiles remain to be read, read another tile and create its DSM
                    if (dsmReadCreateWrite.TileReadIndex < dsmReadCreateWrite.MaxTileIndex)
                    {
                        readSemaphore.Wait(this.CancellationTokenSource.Token);
                        if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                        {
                            // cmdlet is stopping (or, as an edge case, task is cancelled) so point in looking for a tile to read
                            readSemaphore.Release();
                            return;
                        }

                        LasTile? lasTile = null;
                        for (int tileIndex = dsmReadCreateWrite.GetNextTileReadIndexThreadSafe(); tileIndex < dsmReadCreateWrite.MaxTileIndex; tileIndex = dsmReadCreateWrite.GetNextTileReadIndexThreadSafe())
                        {
                            lasTile = lasGrid[tileIndex];
                            if (lasTile == null)
                            {
                                continue; // nothing to do as no tile is present at this grid position
                            }

                            // read DTM tile
                            string tileName = Tile.GetName(lasTile.FilePath);
                            string dtmTilePath = dsmReadCreateWrite.DtmPathIsDirectory ? LasTilesCmdlet.GetRasterTilePath(this.Dtm, tileName) : this.Dtm;
                            dtmTile = RasterBand<float>.CreateOrLoad(dtmTilePath, this.DtmBand, dtmTile);

                            // tilePoints must be in the same CRS as the DTM but can have any extent equal to or smaller than the DSM and DTM tiles
                            if (dtmTile.IsSameExtent(lasTile.GridExtent) == false)
                            {
                                throw new NotSupportedException(tileName + ": DTM tile extent (" + dtmTile.GetExtentString() + ") does not match point cloud tile extent (" + lasTile.GridExtent.GetExtentString() + ").");
                            }

                            // if there's only one point cloud and the DTM extends beyond the cloud expand DTM virtual raster to match the DTM tile extent
                            // This lets a handheld scans be placed within a larger DTM with the DSM expanding beyond the scan to match the DTM.
                            if ((dsmReadCreateWrite.Las.NonNullCells == 1) && (dsmReadCreateWrite.Dsm.NonNullTileCount == 0))
                            {
                                dsmReadCreateWrite.Dsm.TileTransform.Copy(dtmTile.Transform);
                            }

                            // create new DSM tile
                            // Not ideal to call DigitalSurfaceModel..ctor() from within the read semaphore but initial creation is not usefully
                            // possible until the DTM has been read and Reset() of an object pooled DTM tile is inexpensive compared to the .las
                            // read cost.
                            string dsmTilePath = dsmReadCreateWrite.OutputPathIsDirectory ? Path.Combine(this.Dsm, tileName + Constant.File.GeoTiffExtension) : this.Dsm;
                            DigitalSurfaceModel dsmTile = new(dsmTilePath, lasTile, dsmBands, dtmTile, dsmReadCreateWrite.WriteBandPool);

                            // assertions for fully tiled cases
                            // Unlikely to hold for untiled cases, such as UAV flights or handheld scans.
                            Debug.Assert(SpatialReferenceExtensions.IsSameCrs(dsmTile.Crs, dtmTile.Crs) && dsmTile.IsSameExtentAndSpatialResolution(dtmTile));
                            Debug.Assert(SpatialReferenceExtensions.IsSameCrs(dsmTile.Crs, lasTile.GetSpatialReference()) && dsmTile.IsSameExtent(lasTile.GridExtent));

                            // create DSM for this tile
                            using LasReader pointReader = lasTile.CreatePointReader(this.Unbuffered);
                            pointReader.ReadPointsToDsm(lasTile, dsmTile, ref pointReadBuffer, ref dsmSubsurfaceBuffer);
                            readSemaphore.Release(); // exit semaphore as DTM and .las file have both been read

                            dsmTile.OnPointAdditionComplete(dtmTile, this.SubsurfaceGap, dsmSubsurfaceBuffer);

                            (int tileIndexX, int tileIndexY) = lasGrid.ToGridIndices(tileIndex);
                            lock (dsmReadCreateWrite) // all DSM create and write operations lock on DSM virtual raster
                            {
                                dsmReadCreateWrite.Dsm.Add(tileIndexX, tileIndexY, dsmTile);
                                dsmReadCreateWrite.OnTileRead(tileIndexX, tileIndexY);
                                dsmReadCreateWrite.OnTileCreated(tileIndexX, tileIndexY);
                            }

                            // a tile has been read and its DSM created so exit creation loop to check for completable tiles
                            break;
                        }

                        if (lasTile == null)
                        {
                            // end of input tiles has been reached but semaphore was taken and still needs to be released
                            // If semaphore is not released any other worker threads blocked on it can't enter it and thus can't return.
                            readSemaphore.Release();
                        }
                    }
                }
            }, this.CancellationTokenSource);

            int activeReadThreads = readThreads - readSemaphore.CurrentCount;
            TimedProgressRecord progress = new(cmdletName, dsmReadCreateWrite.GetLasReadTileWriteStatusDescription(lasGrid, activeReadThreads, dsmTasks.Count));
            this.WriteProgress(progress);
            while (dsmTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                activeReadThreads = readThreads - readSemaphore.CurrentCount;
                progress.StatusDescription = dsmReadCreateWrite.GetLasReadTileWriteStatusDescription(lasGrid, activeReadThreads, dsmTasks.Count);
                progress.Update(dsmReadCreateWrite.TilesWritten, lasGrid.NonNullCells);
                this.WriteProgress(progress);
            }

            // TODO: generate .vrts

            // release point batches and trigger a gen 2 collection
            // There's easily tens of GB in gen 2 and in the large object heap. Left on its own, the .NET 8 garbage collector often takes
            // minutes to release these but an explicit call to Collect() results in release within a second or so. Requesting compaction
            // brings Windows' display of process memory used into line with the actual size of the managed heap after collection.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            progress.Stopwatch.Stop();
            this.WriteVerbose(dsmReadCreateWrite.CellsWritten.ToString("n0") + " DSM cells from " + lasGrid.NonNullCells + (lasGrid.NonNullCells == 1 ? " tile (" : " tiles (") + (dsmReadCreateWrite.TotalNumberOfPoints / 1E6).ToString("0.0") + " Mpoints) in " + progress.Stopwatch.ToElapsedString() + ": " + dsmReadCreateWrite.TotalPointDataInGB.ToString("0.00") + " GB at " + (dsmReadCreateWrite.TilesWritten / progress.Stopwatch.Elapsed.TotalSeconds).ToString("0.00") + " tiles/s (" + (dsmReadCreateWrite.MeanPointsPerTile / 1E6).ToString("0.0") + " Mpoints/tile, " + (dsmReadCreateWrite.TotalPointDataInGB / progress.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " GB/s).");
            base.ProcessRecord();
        }

        private class DsmReadCreateWrite : TileReadCreateWriteStreaming<LasTileGrid, LasTile, DigitalSurfaceModel>
        {
            public VirtualRaster<DigitalSurfaceModel> Dsm { get; private init; }
            public bool DtmPathIsDirectory { get; private init; }
            public LasTileGrid Las { get; private init; }

            public bool CalculateDsmAndCmmSlopeAndAspect { get; init; }
            public float MeanPointsPerTile { get; private init; }
            public UInt64 TotalNumberOfPoints { get; private init; }
            public float TotalPointDataInGB { get; private init; }

            // dsm.TileGrid checked for null in Create()
            protected DsmReadCreateWrite(LasTileGrid lasGrid, bool[,] unpopulatedTileMapForRead, VirtualRaster<DigitalSurfaceModel> dsm, bool[,] unpopulatedTileMapForCreate, bool[,] unpopulatedTileMapForWrite, bool dsmPathIsDirectory, bool dtmPathIsDirectory)
                : base(lasGrid, unpopulatedTileMapForRead, dsm.TileGrid!, unpopulatedTileMapForCreate, unpopulatedTileMapForWrite, dsmPathIsDirectory)
            {
                Debug.Assert(SpatialReferenceExtensions.IsSameCrs(lasGrid.Crs, dsm.Crs));
                if (lasGrid.IsSameExtentAndSpatialResolution(dsm) == false)
                {
                    throw new ArgumentOutOfRangeException(nameof(dsm), "Point cloud tile grid is " + lasGrid.SizeX + " x " + lasGrid.SizeY + " with extent (" + lasGrid.GetExtentString() + ") while the DSM tile grid is " + dsm.SizeInTilesX + " x " + dsm.SizeInTilesY + " with extent " + dsm.GetExtentString() + ". Are the LAS and DTM tiles matched?");
                }

                this.BypassOutputRasterWriteToDisk = false;
                this.CalculateDsmAndCmmSlopeAndAspect = false;
                this.Dsm = dsm;
                this.DtmPathIsDirectory = dtmPathIsDirectory;
                this.Las = lasGrid;
                this.TileCreationDoesNotRequirePreviousRow = true;

                this.TotalNumberOfPoints = 0;
                long totalTileSizeInBytes = 0;
                for (int lasIndex = 0; lasIndex < lasGrid.Cells; ++lasIndex)
                {
                    LasTile? tile = lasGrid[lasIndex];
                    if (tile != null)
                    {
                        this.TotalNumberOfPoints += tile.Header.GetNumberOfPoints();
                        totalTileSizeInBytes += tile.FileSizeInBytes;
                    }
                }

                this.MeanPointsPerTile = (float)this.TotalNumberOfPoints / (float)lasGrid.NonNullCells;
                this.TotalPointDataInGB = (float)totalTileSizeInBytes / (1024.0F * 1024.0F * 1024.0F);
            }

            public static DsmReadCreateWrite Create(LasTileGrid lasGrid, VirtualRaster<DigitalSurfaceModel> dsm, bool calculateDsmAndCmmSlopeAndAspect, bool dsmPathIsDirectory, bool dtmPathIsDirectory, bool bypassOutputRasterWriteToDisk)
            {
                if (dsm.TileGrid == null)
                {
                    throw new ArgumentOutOfRangeException(nameof(dsm), "DSM's grid has not been built.");
                }

                bool[,] unpopulatedTileMapForRead = lasGrid.GetUnpopulatedCellMap();
                bool[,] unpopulatedTileMapForCreate = ArrayExtensions.Copy(unpopulatedTileMapForRead);
                bool[,] unpopulatedTileMapForWrite = ArrayExtensions.Copy(unpopulatedTileMapForRead);
                return new(lasGrid, unpopulatedTileMapForRead, dsm, unpopulatedTileMapForCreate, unpopulatedTileMapForWrite, dsmPathIsDirectory, dtmPathIsDirectory)
                {
                    BypassOutputRasterWriteToDisk = bypassOutputRasterWriteToDisk,
                    CalculateDsmAndCmmSlopeAndAspect = calculateDsmAndCmmSlopeAndAspect
                };
            }

            public override string GetLasReadTileWriteStatusDescription(LasTileGrid lasGrid, int activeReadThreads, int totalThreads)
            {
                string status = this.TilesRead + (this.TilesRead == 1 ? " cloud read, " : " clouds read, ") +
                                this.Dsm.NonNullTileCount + (this.Dsm.NonNullTileCount == 1 ? " DSM created, " : " DSMs created, ") +
                                this.TilesWritten + " of " + lasGrid.NonNullCells + " tiles " + (this.BypassOutputRasterWriteToDisk ? "completed (" : "written (") + totalThreads +
                                (totalThreads == 1 ? " thread, " : " threads, ") + activeReadThreads + " reading)...";
                return status;
            }

            protected override void OnTileWrite(int tileWriteIndexX, int tileWriteIndexY, DigitalSurfaceModel dsmTileToCmm)
            {
                RasterNeighborhood8<float> dsmNeighborhood = this.Dsm.GetNeighborhood8<float>(tileWriteIndexX, tileWriteIndexY, dsmTileToCmm.Surface.Name);
                Debug.Assert(Object.ReferenceEquals(dsmTileToCmm.Surface, dsmNeighborhood.Center));
                Binomial.Smooth3x3(dsmNeighborhood, dsmTileToCmm.CanopyMaxima3);
                if (this.CalculateDsmAndCmmSlopeAndAspect)
                {
                    RasterNeighborhood8<float> cmmNeighborhood = this.Dsm.GetNeighborhood8<float>(tileWriteIndexX, tileWriteIndexY, dsmTileToCmm.CanopyMaxima3.Name);
                    dsmTileToCmm.CalculateSlopeAndAspect(dsmNeighborhood, cmmNeighborhood);
                }
            }
        }
    }
}
