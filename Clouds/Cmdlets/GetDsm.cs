using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.OSR;
using System;
using System.Diagnostics;
using System.Formats.Tar;
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
        private DsmReadCreateWrite? dsmReadCreateWrite;

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Path to write DSM to as a GeoTIFF or path to a directory to write DSM tiles to. The DSM's cell sizes and positions will be the same as the DTM's.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dsm { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path to a directory containing DTM tiles whose file names match the point cloud tiles. Each DTM must be a single precision floating point raster with ground surface heights in the same CRS as the point cloud tiles. The read capability of the DTM's storage is assumed to be the same as or greater than the DSM's storage.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dtm { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating mean ground elevations. Default is the first band.")]
        [ValidateNotNullOrWhiteSpace]
        public string? DtmBand { get; set; }

        public GetDsm()
        {
            this.dsmReadCreateWrite = null;

            this.Dsm = String.Empty;
            this.Dtm = String.Empty;
            this.DtmBand = null;
            // leave this.MaxThreads at default
        }

        protected override void ProcessRecord()
        {
            if (Fma.IsSupported == false)
            {
                throw new NotSupportedException("Generation of a canopy maxima model requires FMA instructions.");
            }

            HardwareCapabilities hardwareCapabilities = HardwareCapabilities.Current;
            if (this.MaxThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxThreads), "-" + nameof(this.MaxThreads) + " must be at least two.");
            }

            string cmdletName = "Get-Dsm";
            bool dsmPathIsDirectory = Directory.Exists(this.Dsm);
            bool dtmPathIsDirectory = Directory.Exists(this.Dtm);
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, nameof(this.Dsm), dsmPathIsDirectory);

            VirtualRaster<DigitalSurfaceModel> dsm = new(lasGrid);
            this.dsmReadCreateWrite = new(lasGrid, dsm, dsmPathIsDirectory, dtmPathIsDirectory);

            // spin up point cloud read and tile worker threads
            // For small sets of tiles, runtime is dominated by DSM creation latency after tiles are read into memory (streaming read
            // would be helpful but isn't implemented) with filling and sorting the workers' z and source ID lists for each cell being
            // the primary component of runtime. Smaller sets therefore benefit from having up to one worker thread per tile and, up to
            // the point cloud density, greater initial list capacities. For larger sets of tiles, worker requirements set by tile read
            // speed.
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetPointsToLasBandwidth(this.Unbuffered);
            int readThreads = this.GetLasTileReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs, minWorkerThreadsPerReadThread: 0);

            int preferredCompletionThreadsAsymptotic = Int32.Min(this.MaxThreads - readThreads, Int32.Max(readThreads / 3, 2)); // provide at least two completion threads as it appears sometimes beneficial to have more than one
            // but with small numbers of tiles the preferred number of workers increases to reduce overall latency
            int preferredCompletionThreadsLWithFewTiles = Int32.Min(this.MaxThreads, 25 - (int)(1.5F * lasGrid.NonNullCells)); // negative for 17+ tiles
            // default number of workers is the number of tiles or the asymptotic limit with large number of tiles, whichever is less
            // No value in more than one worker per tile.
            int tileCompletionThreads = Int32.Min(lasGrid.NonNullCells, Int32.Max(preferredCompletionThreadsLWithFewTiles, preferredCompletionThreadsAsymptotic));
            Debug.Assert(readThreads + tileCompletionThreads <= this.MaxThreads);

            using SemaphoreSlim readSemaphore = new(initialCount: readThreads, maxCount: readThreads);
            ParallelTasks dsmTasks = new(readThreads + tileCompletionThreads, () =>
            {
                RasterBand<float>? dtmTile = null;
                byte[]? pointReadBuffer = null;
                while (this.dsmReadCreateWrite.TileWritesInitiated < lasGrid.NonNullCells)
                {
                    // create canopy maxima models and write as many tiles as are available for completion
                    // An emphasis on completion minimizes total memory footporint by keeping as few tiles in DDR as is practical.
                    bool noTilesAvailableForWrite = false;
                    while (noTilesAvailableForWrite == false)
                    {
                        DigitalSurfaceModel? dsmTileToCmmAndWrite = null;
                        int tileWriteIndexX = -1;
                        int tileWriteIndexY = -1;
                        lock (this.dsmReadCreateWrite.Dsm)
                        {
                            if (this.dsmReadCreateWrite.TryGetNextTileWriteIndex(out tileWriteIndexX, out tileWriteIndexY, out dsmTileToCmmAndWrite))
                            {
                                ++this.dsmReadCreateWrite.TileWritesInitiated;
                            }
                            else
                            {
                                noTilesAvailableForWrite = true;
                            }
                        }
                        if (dsmTileToCmmAndWrite != null)
                        {
                            VirtualRasterNeighborhood8<float> tileNeighborhood = this.dsmReadCreateWrite.Dsm.GetNeighborhood8<float>(tileWriteIndexX, tileWriteIndexY, dsmTileToCmmAndWrite.Surface.Name);
                            Debug.Assert(Object.ReferenceEquals(dsmTileToCmmAndWrite.Surface, tileNeighborhood.Center));
                            Binomial.Smooth3x3(tileNeighborhood, dsmTileToCmmAndWrite.CanopyMaxima3);

                            if (this.NoWrite == false)
                            {
                                dsmTileToCmmAndWrite.Write(dsmTileToCmmAndWrite.FilePath, this.CompressRasters);
                            }
                            lock (this.dsmReadCreateWrite.Dsm)
                            {
                                // mark tile as written even when NoWrite is set so that virtual raster completion's updated and the tile's returned to the object pool
                                // Since OnTileWritten() returns completed tiles to the DSM object pool the lock taken here must be on the
                                // same object as when tiles are requested from the pool.
                                this.dsmReadCreateWrite.OnTileWritten(tileWriteIndexX, tileWriteIndexY, dsmTileToCmmAndWrite);
                            }
                        }

                        if (this.Stopping || this.dsmReadCreateWrite.CancellationTokenSource.IsCancellationRequested)
                        {
                            return;
                        }
                    }

                    // if all available tiles are completed and tiles remain to be read, read another tile and create its DSM
                    if (this.dsmReadCreateWrite.TileReadIndex < lasGrid.Cells)
                    {
                        readSemaphore.Wait(this.dsmReadCreateWrite.CancellationTokenSource.Token);
                        if (this.Stopping || this.dsmReadCreateWrite.CancellationTokenSource.IsCancellationRequested)
                        {
                            // task is cancelled so point in looking for a tile to read
                            // Also not valid to continue if the read semaphore wasn't entered due to cancellation.
                            return;
                        }

                        LasTile? lasTile = null;
                        for (int tileReadIndex = this.dsmReadCreateWrite.GetNextTileReadIndexThreadSafe(); tileReadIndex < lasGrid.Cells; tileReadIndex = this.dsmReadCreateWrite.GetNextTileReadIndexThreadSafe())
                        {
                            lasTile = lasGrid[tileReadIndex];
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
                            if ((this.dsmReadCreateWrite.Las.NonNullCells == 1) && (this.dsmReadCreateWrite.Dsm.TileCount == 0))
                            {
                                this.dsmReadCreateWrite.Dsm.TileTransform.SetTransform(dtmTile.Transform);
                            }

                            // pull DSM tile from object cache or create a new tile if needed
                            // Not ideal to call DigitalSurfaceModel..ctor() from within the read semaphore but initial creation is not usefully
                            // possible until the DTM has been read and Reset() of an object pooled DTM tile is inexpensive compared to the .las
                            // read cost.
                            DigitalSurfaceModel? dsmTile = null;
                            if (this.dsmReadCreateWrite.TilePool.Count > 0)
                            {
                                lock (this.dsmReadCreateWrite.Dsm)
                                {
                                    // must lock on the same object as the call to OnTileWritten() written above
                                    // If the locks aren't congruent then enqueue/dequeue race conditions can result in multiple threads using the
                                    // same DigitalSurfaceModel instance concurrently.
                                    // No obvious value in checking the pool count again as TryGet() also does that.
                                    this.dsmReadCreateWrite.TilePool.TryGet(out dsmTile);
                                }
                            }

                            string dsmTilePath = dsmReadCreateWrite.OutputPathIsDirectory ? Path.Combine(this.Dsm, tileName + Constant.File.GeoTiffExtension) : this.Dsm;
                            if (dsmTile == null)
                            {
                                dsmTile = new(dsmTilePath, lasTile, dtmTile);
                            }
                            else
                            {
                                dsmTile.Reset(dsmTilePath, lasTile, dtmTile); // update to new path and clear all bands
                            }
                            // assertions for fully tiled cases
                            // Unlikely to hold for untiled cases, such as UAV flights or handheld scans.
                            Debug.Assert(SpatialReferenceExtensions.IsSameCrs(dsmTile.Crs, dtmTile.Crs) && dsmTile.IsSameExtentAndSpatialResolution(dtmTile));
                            Debug.Assert(SpatialReferenceExtensions.IsSameCrs(dsmTile.Crs, lasTile.GetSpatialReference()) && dsmTile.IsSameExtent(lasTile.GridExtent));

                            // create DSM for this tile
                            using LasReader pointReader = lasTile.CreatePointReader(this.Unbuffered);
                            pointReader.ReadPointsToDsm(lasTile, dsmTile, ref pointReadBuffer);
                            readSemaphore.Release(); // exit semaphore as DTM and .las file have both been read

                            this.dsmReadCreateWrite.IncrementTilesReadThreadSafe();

                            dsmTile.OnPointAdditionComplete(dtmTile);
                            lock (this.dsmReadCreateWrite.Dsm) // all DSM create and write operations lock on DSM virtual raster
                            {
                                // (int dsmTileIndexX, int dsmTileIndexY) = this.dsmReadCreateWrite.Dsm.Add(dsmTileToAdd); // sometimes useful to get tile's xy position in grid when debugging
                                this.dsmReadCreateWrite.Dsm.Add(dsmTile);
                            }

                            // a tile has been read and its DSM created so exit creation loop to check for completable tiles
                            break;
                        }

                        if (lasTile == null)
                        {
                            // all input tiles have been read but semaphore 
                            readSemaphore.Release();
                        }
                    }
                }
            }, this.dsmReadCreateWrite.CancellationTokenSource);

            int activeReadThreads = readThreads - readSemaphore.CurrentCount;
            TimedProgressRecord progress = new(cmdletName, this.dsmReadCreateWrite.GetLasReadTileWriteStatusDescription(lasGrid, activeReadThreads, dsmTasks.Count));
            this.WriteProgress(progress);
            while (dsmTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                activeReadThreads = readThreads - readSemaphore.CurrentCount;
                progress.StatusDescription = this.dsmReadCreateWrite.GetLasReadTileWriteStatusDescription(lasGrid, activeReadThreads, dsmTasks.Count);
                progress.Update(this.dsmReadCreateWrite.TilesWritten, lasGrid.NonNullCells);
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
            this.WriteVerbose(this.dsmReadCreateWrite.CellsWritten.ToString("n0") + " DSM cells from " + lasGrid.NonNullCells + (lasGrid.NonNullCells == 1 ? " tile (" : " tiles (") + (this.dsmReadCreateWrite.TotalNumberOfPoints / 1E6).ToString("0.0") + " Mpoints) in " + progress.Stopwatch.ToElapsedString() + ": " + this.dsmReadCreateWrite.TotalPointDataInGB.ToString("0.00") + " GB at " + (this.dsmReadCreateWrite.TilesWritten / progress.Stopwatch.Elapsed.TotalSeconds).ToString("0.00") + " tiles/s (" + (this.dsmReadCreateWrite.MeanPointsPerTile / 1E6).ToString("0.0") + " Mpoints/tile, " + (this.dsmReadCreateWrite.TotalPointDataInGB / progress.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " GB/s).");
            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            this.dsmReadCreateWrite?.CancellationTokenSource.Cancel();
            base.StopProcessing();
        }

        private class DsmReadCreateWrite : TileReadCreateWriteStreaming<LasTileGrid, LasTile, DigitalSurfaceModel>
        {
            public VirtualRaster<DigitalSurfaceModel> Dsm { get; private init; }
            public bool DtmPathIsDirectory { get; private init; }
            public LasTileGrid Las { get; private init; }

            public float MeanPointsPerTile { get; private init; }
            public int TileWritesInitiated { get; set; }
            public UInt64 TotalNumberOfPoints { get; private init; }
            public float TotalPointDataInGB { get; private init; }

            public DsmReadCreateWrite(LasTileGrid lasGrid, VirtualRaster<DigitalSurfaceModel> dsm, bool dsmPathIsDirectory, bool dtmPathIsDirectory)
                : base(lasGrid, dsm, dsmPathIsDirectory)
            {
                Debug.Assert(SpatialReferenceExtensions.IsSameCrs(lasGrid.Crs, dsm.Crs));
                if (lasGrid.IsSameExtentAndSpatialResolution(dsm) == false)
                {
                    throw new ArgumentOutOfRangeException(nameof(dsm), "Point cloud tile grid is " + lasGrid.SizeX + " x " + lasGrid.SizeY + " with extent (" + lasGrid.GetExtentString() + ") while the DSM tile grid is " + dsm.VirtualRasterSizeInTilesX + " x " + dsm.VirtualRasterSizeInTilesY + " with extent " + dsm.GetExtentString() + ". Are the LAS and DTM tiles matched?");
                }

                this.Dsm = dsm;
                this.DtmPathIsDirectory = dtmPathIsDirectory;
                this.Las = lasGrid;
                this.TileWritesInitiated = 0;

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

            public override string GetLasReadTileWriteStatusDescription(LasTileGrid lasGrid, int activeReadThreads, int totalThreads)
            {
                string status = this.TilesRead + (this.TilesRead == 1 ? " cloud read, " : " clouds read, ") +
                                this.Dsm.TileCount + (this.Dsm.TileCount == 1 ? " DSM created, " : " DSMs created, ") +
                                this.TilesWritten + " of " + lasGrid.NonNullCells + " tiles written (" + totalThreads +
                                (totalThreads == 1 ? " thread, " : " threads, ") + activeReadThreads + " reading)...";
                return status;
            }
        }
    }
}
