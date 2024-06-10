using Mars.Clouds.Cmdlets.Drives;
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
using System.Threading.Tasks;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Dsm")]
    public class GetDsm : LasTilesToTilesCmdlet
    {
        private const int DsmCreationTimeoutInMs = 1000;

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Path to write DSM to as a GeoTIFF or path to a directory to write DSM tiles to. The DSM's cell sizes and positions will be the same as the DTM's.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dsm { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path to a directory containing DTM tiles whose file names match the point cloud tiles. Each DTM must be a single precision floating point raster with ground surface heights in the same CRS as the point cloud tiles.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dtm { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating mean ground elevations. Default is the first band.")]
        [ValidateNotNullOrWhiteSpace]
        public string? DtmBand { get; set; }

        [Parameter(HelpMessage = "Vertical distance beyond which groups of points are considered distinct layers. Default is 3 m for metric point clouds and 10 feet for point clouds with English units.")]
        [ValidateRange(0.0F, 500.0F)] // arbitrary upper bound
        public float LayerSeparation { get; set; }

        [Parameter(HelpMessage = "Number of threads, out of -MaxThreads, to use for reading tiles. Default is automatic estimation, which will typically choose single read thread.")]
        [ValidateRange(1, 32)] // arbitrary upper bound
        public int ReadThreads { get; set; }

        public GetDsm()
        {
            this.Dsm = String.Empty;
            this.Dtm = String.Empty;
            this.DtmBand = null;
            this.LayerSeparation = -1.0F;
            // leave this.MaxThreads at default
            this.ReadThreads = -1;
        }

        private void CreateAndWriteTiles(DsmReadCreateWrite dsmReadCreateWrite)
        {
            // each DSM tile creation requires a corresponding point list and DTM tile
            // Point lists are large, heavy objects and are therefore reset and resued on each tile read to amortize and mitigate List<>
            // creation and resize costs. DTMs are comparatively small (often 4-40 MB versus a few hundred MB to a few GB of List<>s) but
            // are easily reused rather than reallocated.
            PointListGridZs? aerialPointZs = null;
            RasterBand<float>? dtmTile = null;

            // create and write tiles
            // Loop continues for as long as there is new work to begin. This structure allows worker threads to exit after writing the
            // last tile available to them. TryTake() returns immediately once adding is completed so, with the current code, if threads
            // didn't exit they'd spin on TryGetNextTileWriteIndex() until the last tile write completed.
            while (dsmReadCreateWrite.TileWritesInitiated < dsmReadCreateWrite.Las.NonNullCells)
            {
                // create DSM tiles as they become available
                if (dsmReadCreateWrite.LoadedTiles.TryTake(out (string Name, PointList<PointBatchXyzcs> Points) lasTile, GetDsm.DsmCreationTimeoutInMs, dsmReadCreateWrite.CancellationTokenSource.Token))
                {
                    try
                    {
                        // load DTM tile
                        string dtmTilePath = dsmReadCreateWrite.DtmPathIsDirectory ? LasTilesCmdlet.GetRasterTilePath(this.Dtm, lasTile.Name) : this.Dtm;
                        dtmTile = RasterBand<float>.CreateOrLoad(dtmTilePath, this.DtmBand, dtmTile);

                        // create DSM tile
                        // DigitalSurfaceModel..ctor() verifies point and DTM tiles have compatible extents in the same CRS.
                        PointList<PointBatchXyzcs> tilePoints = lasTile.Points;
                        PointListGridZs.CreateRecreateOrReset(dtmTile, dsmReadCreateWrite.MeanPointsPerTile, ref aerialPointZs);

                        string dsmTilePath = dsmReadCreateWrite.OutputPathIsDirectory ? Path.Combine(this.Dsm, lasTile.Name + Constant.File.GeoTiffExtension) : this.Dsm;
                        if (dsmReadCreateWrite.TilePool.TryGetThreadSafe(out DigitalSurfaceModel? dsmTileToAdd) == false)
                        {
                            dsmTileToAdd = new(dsmTilePath, tilePoints, dtmTile, aerialPointZs, this.LayerSeparation);
                        }
                        else
                        {
                            dsmTileToAdd.ResetAllBandsToDefaultValues();
                        }
                        int dsmTileIndexX;
                        int dsmTileIndexY;
                        lock (dsmReadCreateWrite.Dsm) // all DSM create and write operations lock on DSM virtual raster
                        {
                            // if there's only one point cloud and the DTM extends beyond the cloud expand DTM virtual raster to match the DTM tile extent
                            // This lets a handheld scans be placed within a larger DTM with the DSM expanding beyond the scan to match the DTM.
                            if ((dsmReadCreateWrite.Las.NonNullCells == 1) && (dsmReadCreateWrite.Dsm.TileCount == 0))
                            {
                                dsmReadCreateWrite.Dsm.TileTransform.SetTransform(dtmTile.Transform);
                            }
                            (dsmTileIndexX, dsmTileIndexY) = dsmReadCreateWrite.Dsm.Add(dsmTileToAdd);
                        }

                        dsmReadCreateWrite.PointBatchPool.ReturnThreadSafe(lasTile.Points);
                    }
                    catch (Exception exception)
                    {
                        throw new TaskCanceledException("Failed to create DSM tile '" + lasTile.Name + "' due to error.", exception, dsmReadCreateWrite.CancellationTokenSource.Token);
                    }

                    if (this.Stopping || dsmReadCreateWrite.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }

                // check if any canopy maxima models can be generated and tiles written
                bool writeTile = false;
                int tileWriteIndexX = -1;
                int tileWriteIndexY = -1;
                DigitalSurfaceModel? dsmTileToWrite = null;
                try
                {
                    lock (dsmReadCreateWrite.Dsm)
                    {
                        writeTile = dsmReadCreateWrite.TryGetNextTileWriteIndex(out tileWriteIndexX, out tileWriteIndexY, out dsmTileToWrite);
                        if (writeTile)
                        {
                            ++dsmReadCreateWrite.TileWritesInitiated;
                        }
                    }
                    if (writeTile)
                    {
                        Debug.Assert(dsmTileToWrite != null); // nullibility bug: [NotNullWhen(true)] doesn't get tracked correctly here

                        VirtualRasterNeighborhood8<float> tileNeighborhood = dsmReadCreateWrite.Dsm.GetNeighborhood8<float>(tileWriteIndexX, tileWriteIndexY, dsmTileToWrite.Surface.Name);
                        Debug.Assert(Object.ReferenceEquals(dsmTileToWrite.Surface, tileNeighborhood.Center));
                        Binomial.Smooth3x3(tileNeighborhood, dsmTileToWrite.CanopyMaxima3);

                        string tileName = dsmReadCreateWrite.Dsm.GetTileName(tileWriteIndexX, tileWriteIndexY);
                        string dsmTilePath = dsmReadCreateWrite.OutputPathIsDirectory ? Path.Combine(this.Dsm, tileName + Constant.File.GeoTiffExtension) : this.Dsm;
                        dsmTileToWrite.Write(dsmTilePath, this.CompressRasters);

                        lock (dsmReadCreateWrite.Dsm)
                        {
                            dsmReadCreateWrite.OnTileWritten(tileWriteIndexX, tileWriteIndexY, dsmTileToWrite);
                        }
                    }

                    if (this.Stopping || dsmReadCreateWrite.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }
                catch (Exception exception)
                {
                    throw new TaskCanceledException("Failed to obtain a DSM tile to write or failed to write DSM tile '" + dsmTileToWrite?.FilePath + "' at tile grid position " + tileWriteIndexX + ", " + tileWriteIndexY + ").", exception, dsmReadCreateWrite.CancellationTokenSource.Token);
                }
            }
        }

        protected override void ProcessRecord()
        {
            if (this.MaxThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxThreads), "-" + nameof(this.MaxThreads) + " must be at least two.");
            }
            if (this.MaxThreads > this.MaxPointTiles)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxPointTiles), "-" + nameof(this.MaxPointTiles) + " must be greater than or equal to the maximum number of threads (" + this.MaxThreads + ") as each thread requires a tile to work with.");
            }
            if (this.ReadThreads >= this.MaxThreads)
            {
                throw new ParameterOutOfRangeException(nameof(this.ReadThreads), "-" + nameof(this.ReadThreads) + " (" + this.ReadThreads + " threads) must be less than -" + nameof(this.MaxThreads) + " (" + this.MaxThreads + " threads) in order for at least one worker thread to process the tiles being read.");
            }
            if (Fma.IsSupported == false)
            {
                throw new NotSupportedException("Generation of a canopy maxima model requires FMA instructions.");
            }

            string cmdletName = "Get-Dsm";
            bool dsmPathIsDirectory = Directory.Exists(this.Dsm);
            bool dtmPathIsDirectory = Directory.Exists(this.Dtm);
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, nameof(this.Dsm), dsmPathIsDirectory);

            if (this.LayerSeparation < 0.0F)
            {
                double crsLinearUnits = lasGrid.Crs.GetLinearUnits();
                this.LayerSeparation = crsLinearUnits == 1.0 ? 3.0F : 10.0F; // 3 m or 10 feet
            }

            VirtualRaster<DigitalSurfaceModel> dsm = new(lasGrid);
            DsmReadCreateWrite dsmReadCreateWrite = new(lasGrid, dsm, this.MaxPointTiles, dsmPathIsDirectory, dtmPathIsDirectory);

            // spin up point cloud read and tile worker threads
            // For small sets of tiles, runtime is dominated by DSM creation latency after tiles are read into memory (streaming read
            // would be helpful but isn't implemented) with filling and sorting the workers' z and source ID lists for each cell being
            // the primary component of runtime. Smaller sets therefore benefit from having up to one worker thread per tile and, up to
            // the point cloud density, greater initial list capacities. For larger sets of tiles, worker requirements set by tile read
            // speed.
            //
            // older performance baselines, 5950X with Elliott 2021 tiles (914 x 914 m @ ~35 points/m²) with LasReader @ 2.0 GB/s max
            //  ~1.9 GB/s read from 2TB SN770 on CPU lanes @ 1 read thread -> ~105 GB working set, 11 worker threads, 0.60 tiles/s (153 tiles in 4:14), tupled lists in PointListGridZs, initial capacity 64 points (~6% degradation at 128 points)
            //  ~1.7 GB/s read from 2TB SN770 on CPU lanes @ 1 read thread -> ~105 GB working set, 11 worker threads, 0.54 tiles/s (153 tiles in 4:45), tupled lists in PointListGridZs, initial capacity 32 points
            //  ~1.7 GB/s read from 2TB SN770 on CPU lanes @ 1 read thread -> ~105 GB working set, 11 worker threads, 0.49 tiles/s (153 tiles in 5:13), tupled lists in PointListGridZs, initial capacity 16 points
            //  ~1.1 GB/s read from 2TB SN770 on CPU lanes @ 1 read thread -> ~105 GB working set, 11 worker threads, 0.41 tiles/s (153 tiles in 6:21), z + point source IDs lists in PointListGridZs, initial capacity 16 points
            //   250 MB/s read from 3.5 inch hard drive @ 1 read thread -> ~64 GB system memory used, one, maybe two worker threads
            DriveCapabilities driveCapabilities = DriveCapabilities.Create(this.Las);
            int readThreads = this.ReadThreads;
            if (this.ReadThreads == -1)
            {
                readThreads = driveCapabilities.MediaType == MediaType.HardDrive ? driveCapabilities.NumberOfDataCopies : 1;
            }

            // upper bound on workers is set by -MaxThreads and available memory
            // TODO: estimate memory requirements from point tiles (also sample DTM resolution?)
            int usablePhysicalMemoryInGB = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024 * 1024)) - 8; // set aside 8 GB for operating system and other programs
            int maxWorkerThreads = Int32.Max(Int32.Min(this.MaxThreads - readThreads, usablePhysicalMemoryInGB / 6), 1); // for now, assume 6 GB/thread, guarantee at least one worker thread
            // minimum bound on workers is at least two, but prefer a minimum of two for margin and enough to fully utilize the read threads
            // Assumption read and processing bandwidths scale similarly across different hardware configs and therefore that the ratio between
            // read and worker threads is fairly stable.
            float readBandwidthInGBs = Single.Min(LasReader.ReadPointsToXyzcsSpeedInGBs * readThreads, driveCapabilities.GetSequentialCapabilityInGBs());
            int preferredWorkerThreadsAsymptotic = Int32.Min(maxWorkerThreads, Int32.Max(2, (int)(readBandwidthInGBs / 0.2F + 0.5F)));
            // but with small numbers of tiles the preferred number of workers increases to reduce overall latency
            int preferredWorkerThreadsLimitedTiles = Int32.Min(maxWorkerThreads, 25 - (int)(1.5F * lasGrid.NonNullCells)); // negative for 17+ tiles
            // default number of workers is the number of tiles or the asymptotic limit with large number of tiles, whichever is less
            // No value in more than one worker per tile.
            int workerThreads = Int32.Min(lasGrid.NonNullCells, Int32.Max(preferredWorkerThreadsLimitedTiles, preferredWorkerThreadsAsymptotic));

            Task[] dsmTasks = new Task[readThreads + workerThreads];
            for (int readThread = 0; readThread < readThreads; ++readThread)
            {
                dsmTasks[readThread] = Task.Run(() => this.ReadLasTiles(lasGrid, GetDsm.ReadTile, dsmReadCreateWrite), dsmReadCreateWrite.CancellationTokenSource.Token);
            }
            for (int workerThread = readThreads; workerThread < dsmTasks.Length; ++workerThread)
            {
                dsmTasks[workerThread] = Task.Run(() => this.CreateAndWriteTiles(dsmReadCreateWrite), dsmReadCreateWrite.CancellationTokenSource.Token);
            }
            // for standalone testing of read performance
            // Task[] dsmTasks = [ Task.Run(() => this.ReadLasTiles(lasGrid, this.ReadTile, dsmReadCreateWrite), dsmReadCreateWrite.CancellationTokenSource.Token) ];

            TimedProgressRecord progress = this.WaitForLasReadTileWriteTasks(cmdletName, dsmTasks, lasGrid, dsmReadCreateWrite);

            // TODO: generate .vrts

            // release point batches and trigger a gen 2 collection
            // There's easily tens of GB in gen 2 and in the large object heap. Left on its own, the .NET 8 garbage collector often takes
            // minutes to release these but an explicit call to Collect() results in release within a second or so. Requesting compaction
            // brings Windows' display of process memory used into line with the actual size of the managed heap after collection.
            dsmReadCreateWrite.OnTileWriteComplete();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            progress.Stopwatch.Stop();
            string elapsedTimeFormat = progress.Stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose(dsmReadCreateWrite.CellsWritten.ToString("n0") + " DSM cells from " + lasGrid.NonNullCells + (lasGrid.NonNullCells == 1 ? " tile (" : " tiles (") + (dsmReadCreateWrite.TotalNumberOfPoints / 1E6).ToString("0.0") + " Mpoints) in " + progress.Stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + dsmReadCreateWrite.TotalPointDataInGB.ToString("0.00") + " GB at " + (dsmReadCreateWrite.TilesWritten / progress.Stopwatch.Elapsed.TotalSeconds).ToString("0.00") + " tiles/s (" + (dsmReadCreateWrite.MeanPointsPerTile / 1E6).ToString("0.0") + " Mpoints/tile, " + (dsmReadCreateWrite.TotalPointDataInGB / progress.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " GB/s).");
            base.ProcessRecord();
        }

        private static PointList<PointBatchXyzcs> ReadTile(LasTile lasTile, DsmReadCreateWrite dsmReadCreateWrite)
        {
            using LasReader pointReader = lasTile.CreatePointReader();
            return pointReader.ReadPoints(lasTile, dsmReadCreateWrite.PointBatchPool);
        }

        private class DsmReadCreateWrite : TileReadCreateWriteStreaming<LasTileGrid, LasTile, PointList<PointBatchXyzcs>, DigitalSurfaceModel>
        {
            public VirtualRaster<DigitalSurfaceModel> Dsm { get; private init; }
            public bool DtmPathIsDirectory { get; private init; }
            public LasTileGrid Las { get; private init; }

            public float MeanPointsPerTile { get; private init; }
            public ObjectPool<PointBatchXyzcs> PointBatchPool { get; private init; }
            public int TileWritesInitiated { get; set; }
            public UInt64 TotalNumberOfPoints { get; private init; }
            public float TotalPointDataInGB { get; private init; }

            public DsmReadCreateWrite(LasTileGrid lasGrid, VirtualRaster<DigitalSurfaceModel> dsm, int maxSimultaneouslyLoadedTiles, bool dsmPathIsDirectory, bool dtmPathIsDirectory)
                : base(lasGrid, dsm, maxSimultaneouslyLoadedTiles, dsmPathIsDirectory)
            {
                // TODO: lasGrid.IsSameExtentAndResolution(dsm)
                if ((lasGrid.SizeX != dsm.VirtualRasterSizeInTilesX) && (lasGrid.SizeY == dsm.VirtualRasterSizeInTilesY))
                {
                    throw new ArgumentOutOfRangeException(nameof(dsm), "Point cloud tile grid is " + lasGrid.SizeX + " x " + lasGrid.SizeY + " while the DSM tile grid is " + dsm.VirtualRasterSizeInTilesX + " x " + dsm.VirtualRasterSizeInTilesY + ". Are the LAS and DTM tile specifications matched? (This is a temporary requirement pending more flexible tile instantiation.)");
                }

                this.Dsm = dsm;
                this.DtmPathIsDirectory = dtmPathIsDirectory;
                this.Las = lasGrid;
                this.PointBatchPool = new();
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

            public override string GetLasReadTileWriteStatusDescription(LasTileGrid lasGrid)
            {
                string status = this.TilesRead + (this.TilesRead == 1 ? " point cloud tile read, " : " point cloud tiles read, ") +
                                this.Dsm.TileCount + (this.Dsm.TileCount == 1 ? " DSM tile created, " : " DSM tiles created, ") +
                                this.TilesWritten + " of " + lasGrid.NonNullCells + " tiles written...";
                return status;
            }

            public void OnTileWriteComplete()
            {
                this.PointBatchPool.Clear();
            }
        }
    }
}
