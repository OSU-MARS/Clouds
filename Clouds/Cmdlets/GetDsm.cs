using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Management.Automation;
using System.Reflection.Metadata;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Dsm")]
    public class GetDsm : LasTilesToTilesCmdlet
    {
        private const int DsmCreationTimeoutInMs = 1000;

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to write DSM to as a GeoTIFF or 2,3) path to a directory to write DSM tiles to. The DSM's cell sizes and positions will be the same as the DTM's.")]
        [ValidateNotNullOrWhiteSpace]
        public string? Dsm { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path to a directory containing DTM tiles whose file names match the point cloud tiles. Each DTM must be a single precision floating point raster with ground surface heights in the same CRS as the point cloud tiles.")]
        [ValidateNotNullOrWhiteSpace]
        public string? Dtm { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating mean ground elevations. Default is the first band.")]
        [ValidateNotNullOrWhiteSpace]
        public string? DtmBand { get; set; }

        [Parameter(HelpMessage = "Number of points each worker thread initially allocates space for per cell. This is a relatively unimportant performance tuning parameter which affects runtimes by ~20% when the number of tiles is comparable to the number of worker threads. Higher values reduce reallocations as workers' temporary storage expands to accommodate cells with larger numbers of points. Smaller values may lower memory requirements, especially on sparse point clouds. Default is 16 points per cell.")]
        [ValidateRange(1, 1024)]
        public int InitialCellCapacity;

        [Parameter(HelpMessage = "Vertical distance beyond which groups of points are considered distinct layers. Default is 3 m for metric point clouds and 10 feet for point clouds with English units.")]
        [ValidateRange(0.0F, 500.0F)] // arbitrary upper bound
        public float LayerSeparation { get; set; }

        [Parameter(HelpMessage = "Number of threads, out of -MaxThreads, to use for reading tiles.")]
        [ValidateRange(1, 32)]
        public int ReadThreads { get; set; }

        public GetDsm()
        {
            // this.Dsm is mandatory
            // this.Dtm is mandatory
            this.DtmBand = null;
            this.InitialCellCapacity = 16;
            this.LayerSeparation = -1.0F;
            // leave this.MaxThreads at default for DTM read
            this.ReadThreads = 1;
        }

        private void CreateAndWriteTiles(DsmReadCreateWrite dsmReadCreateWrite)
        {
            Debug.Assert((this.Dsm != null) && (this.Dtm != null));
            PointListGridZs? aerialPointZs = null;

            // create and write tiles
            // Loop continues for as long as there is new work to begin. This structure allows worker threads to exit after writing the
            // last tile available to them. TryTake() returns immediately once adding is completed so, with the current code, if threads
            // didn't exit they'd spin on TryGetNextTileWriteIndex() until the last tile write completed.
            while (dsmReadCreateWrite.TileWritesInitiated < dsmReadCreateWrite.Las.NonNullCells)
            {
                // create DSM tiles while additional 
                // MoveNext() blocks until all tiles have been read becaues LasTilesToTilesCmdlet.ReadTiles() doesn't call
                // CompleteAdding() until after the last read finishes.
                if (dsmReadCreateWrite.LoadedTiles.TryTake(out (string Name, PointListXyzcs Points) lasTile, GetDsm.DsmCreationTimeoutInMs, dsmReadCreateWrite.CancellationTokenSource.Token))
                {
                    try
                    {
                        // load DTM tile
                        string dtmTilePath = Path.Combine(this.Dtm, lasTile.Name + Constant.File.GeoTiffExtension);
                        RasterBand<float> dtmTile = Raster<float>.ReadBand(dtmTilePath, this.DtmBand);
                        PointListXyzcs tilePoints = lasTile.Points;

                        // create DSM tile
                        // DigitalSurfaceModel..ctor() verifies point and DTM tiles have the same extent in the same CRS.
                        PointListGridZs.CreateRecreateOrReset(dtmTile, this.InitialCellCapacity, ref aerialPointZs);

                        string dsmTilePath = dsmReadCreateWrite.OutputPathIsDirectory ? Path.Combine(this.Dsm, lasTile.Name + Constant.File.GeoTiffExtension) : this.Dsm;
                        DigitalSurfaceModel dsmTileToAdd = new(dsmTilePath, tilePoints, dtmTile, aerialPointZs, this.LayerSeparation);
                        int dsmTileIndexX;
                        int dsmTileIndexY;
                        lock (dsmReadCreateWrite.Dsm)
                        {
                            (dsmTileIndexX, dsmTileIndexY) = dsmReadCreateWrite.Dsm.Add(lasTile.Name, dsmTileToAdd);
                        }
                    }
                    catch (Exception exception)
                    {
                        throw new TaskCanceledException("Failed to create DSM tile '" + lasTile.Name + "'.", exception, dsmReadCreateWrite.CancellationTokenSource.Token);
                    }

                    if (this.Stopping || dsmReadCreateWrite.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }

                // check if any canopy maxima models can be generated and tiles written
                bool writeTile;
                int tileWriteIndexX;
                int tileWriteIndexY;
                DigitalSurfaceModel? dsmTileToWrite;
                lock (dsmReadCreateWrite)
                {
                    writeTile = dsmReadCreateWrite.TryGetNextTileWriteIndex(out tileWriteIndexX, out tileWriteIndexY, out dsmTileToWrite);
                    if (writeTile)
                    {
                        ++dsmReadCreateWrite.TileWritesInitiated;
                    }
                }
                if (writeTile)
                {
                    try
                    {
                        Debug.Assert(dsmTileToWrite != null); // nullibility bug: [NotNullWhen(true)] doesn't get tracked correctly here

                        VirtualRasterNeighborhood8<float> tileNeighborhood = dsmReadCreateWrite.Dsm.GetNeighborhood8<float>(tileWriteIndexX, tileWriteIndexY, dsmTileToWrite.Surface.Name);
                        Debug.Assert(Object.ReferenceEquals(dsmTileToWrite.Surface, tileNeighborhood.Center));
                        Binomial.Smooth3x3(tileNeighborhood, dsmTileToWrite.CanopyMaxima3);

                        string tileName = dsmReadCreateWrite.Dsm.GetTileName(tileWriteIndexX, tileWriteIndexY);
                        string dsmTilePath = dsmReadCreateWrite.OutputPathIsDirectory ? Path.Combine(this.Dsm, tileName + Constant.File.GeoTiffExtension) : this.Dsm;
                        dsmTileToWrite.Write(dsmTilePath, this.CompressRasters);

                        lock (dsmReadCreateWrite)
                        {
                            dsmReadCreateWrite.OnTileWritten(tileWriteIndexX, tileWriteIndexY, dsmTileToWrite);
                        }
                    }
                    catch (Exception exception)
                    {
                        throw new TaskCanceledException("Failed to write DSM tile '" + lasTile.Name + "'.", exception, dsmReadCreateWrite.CancellationTokenSource.Token);
                    }

                    if (this.Stopping || dsmReadCreateWrite.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }

        protected override void ProcessRecord()
        {
            Debug.Assert((this.Dsm != null) && (this.Dtm != null));
            if (this.MaxThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxThreads), "-" + nameof(this.MaxThreads) + " must be at least two.");
            }
            if (this.MaxThreads > this.MaxTiles)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxTiles), "-" + nameof(this.MaxTiles) + " must be greater than or equal to the maximum number of threads (" + this.MaxThreads + ") as each thread requires a tile to work with.");
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
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, nameof(this.Dsm), dsmPathIsDirectory);

            if (this.LayerSeparation < 0.0F)
            {
                double crsLinearUnits = lasGrid.Crs.GetLinearUnits();
                this.LayerSeparation = crsLinearUnits == 1.0 ? 3.0F : 10.0F; // 3 m or 10 feet
            }

            VirtualRaster<DigitalSurfaceModel> dsm = new(lasGrid);
            DsmReadCreateWrite dsmReadCreateWrite = new(lasGrid, dsm, this.MaxTiles, dsmPathIsDirectory);

            // spin up point cloud read and tile worker threads
            // For small sets of tiles, runtime is dominated by DSM creation latency after tiles are read into memory (streaming read
            // would be helpful but isn't implemented) with filling and sorting the workers' z and source ID lists for each cell being
            // the primary component of runtime. Smaller sets therefore benefit from having up to one worker thread per tile and, up to
            // the point cloud density, greater initial list capacities. For larger sets of tiles, worker requirements set by tile read
            // speed.
            //
            // Basic perf timings (5950X):
            //   unconstrained single threaded read => 2.0 GB/s
            //   3.5 inch hard drive @ 250 MB/s => TBD worker threads
            int workerThreads = Int32.Min(this.MaxThreads - this.ReadThreads, lasGrid.NonNullCells);
            Task[] dsmTasks = new Task[this.ReadThreads + workerThreads];
            for (int readThread = 0; readThread < this.ReadThreads; ++readThread)
            {
                dsmTasks[readThread] = Task.Run(() => this.ReadLasTiles(lasGrid, this.ReadTile, dsmReadCreateWrite), dsmReadCreateWrite.CancellationTokenSource.Token);
            }
            for (int workerThread = this.ReadThreads; workerThread < dsmTasks.Length; ++workerThread)
            {
                dsmTasks[workerThread] = Task.Run(() => this.CreateAndWriteTiles(dsmReadCreateWrite), dsmReadCreateWrite.CancellationTokenSource.Token);
            }
            // for standalone testing of read performance
            //Task[] dsmTasks = [ Task.Run(() => this.ReadLasTiles(lasGrid, this.ReadTile, dsmReadCreateWrite), dsmReadCreateWrite.CancellationTokenSource.Token) ];

            this.WaitForLasReadTileWriteTasks("Get-Dsm", dsmTasks, lasGrid, dsmReadCreateWrite);
            // TODO: generate .vrts

            string elapsedTimeFormat = dsmReadCreateWrite.Stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Generated " + dsmReadCreateWrite.CellsWritten.ToString("n0") + " DSM cells from " + lasGrid.NonNullCells + " point cloud tiles in " + dsmReadCreateWrite.Stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + (dsmReadCreateWrite.TilesWritten / dsmReadCreateWrite.Stopwatch.Elapsed.TotalSeconds).ToString("0.00") + " tiles/s.");
            base.ProcessRecord();
        }

        private PointListXyzcs ReadTile(LasTile lasTile, DsmReadCreateWrite dsmReadCreateWrite)
        {
            using LasReader pointReader = lasTile.CreatePointReader();
            return pointReader.ReadPoints(lasTile);
        }

        private class DsmReadCreateWrite : TileReadWrite<PointListXyzcs>
        {
            private readonly bool[,] dsmCompletedWriteMap;
            private int dsmPendingCreationIndexY;
            private int dsmPendingFreeIndexY;
            private int dsmPendingWriteCompletionIndexY;
            private int dsmPendingWriteIndexX;
            private int dsmPendingWriteIndexY;

            public VirtualRaster<DigitalSurfaceModel> Dsm { get; private init; }
            public LasTileGrid Las { get; private init; }
            public int TileWritesInitiated { get; set; }

            public DsmReadCreateWrite(LasTileGrid lasGrid, VirtualRaster<DigitalSurfaceModel> dsm, int maxSimultaneouslyLoadedTiles, bool dsmPathIsDirectory)
                : base(maxSimultaneouslyLoadedTiles, dsm.TileSizeInCellsX, dsm.TileSizeInCellsY, dsmPathIsDirectory)
            {
                // TODO: lasGrid.IsSameExtentAndResolution(dsm)
                if ((lasGrid.SizeX != dsm.VirtualRasterSizeInTilesX) && (lasGrid.SizeY == dsm.VirtualRasterSizeInTilesY))
                {
                    throw new ArgumentOutOfRangeException(nameof(dsm), "Point cloud tile grid is " + lasGrid.SizeX + " x " + lasGrid.SizeY + " while the DSM tile grid is " + dsm.VirtualRasterSizeInTilesX + " x " + dsm.VirtualRasterSizeInTilesY + ". Are the LAS and DTM tile specifications matched? (This is a temporary requirement pending more flexible tile instantiation.)");
                }

                this.dsmCompletedWriteMap = lasGrid.GetUnpopulatedCellMap();
                this.dsmPendingCreationIndexY = 0;
                this.dsmPendingFreeIndexY = 0;
                this.dsmPendingWriteCompletionIndexY = 0;
                this.dsmPendingWriteIndexX = 0;
                this.dsmPendingWriteIndexY = 0;

                this.Dsm = dsm;
                this.Las = lasGrid;
                this.TileWritesInitiated = 0;
            }

            public override string GetLasReadTileWriteStatusDescription(LasTileGrid lasGrid)
            {
                string status = this.TilesLoaded + (this.TilesLoaded == 1 ? " point cloud tile read, " : " point cloud tiles read, ") +
                                this.Dsm.TileCount + (this.Dsm.TileCount == 1 ? " DSM tile created, " : " DSM tiles created, ") +
                                this.TilesWritten + " of " + lasGrid.NonNullCells + " tiles written...";
                return status;
            }

            public void OnTileWritten(int tileWriteIndexX, int tileWriteIndexY, DigitalSurfaceModel dsmTileWritten)
            {
                this.dsmCompletedWriteMap[tileWriteIndexX, tileWriteIndexY] = true;

                // scan to see if creation of one or more rows of DSM tiles has been completed since the last call
                // Similar to code in TryGetNextTileWriteIndex().
                for (; this.dsmPendingWriteCompletionIndexY < this.Dsm.VirtualRasterSizeInTilesY; ++this.dsmPendingWriteCompletionIndexY)
                {
                    bool dsmRowIncompletelyWritten = false;
                    for (int xIndex = 0; xIndex < this.Dsm.VirtualRasterSizeInTilesX; ++xIndex)
                    {
                        if (this.dsmCompletedWriteMap[xIndex, this.dsmPendingWriteCompletionIndexY] == false)
                        {
                            dsmRowIncompletelyWritten = true;
                            break;
                        }
                    }

                    if (dsmRowIncompletelyWritten)
                    {
                        break;
                    }
                }

                int dsmWriteCompletedIndexY = this.dsmPendingWriteCompletionIndexY; // if DSM is fully written then all its tiles can be freed
                if (this.dsmPendingWriteCompletionIndexY < this.Dsm.VirtualRasterSizeInTilesY)
                {
                    --dsmWriteCompletedIndexY; // row n of DSM isn't fully written so row n - 1 cannot be written without impairing canopy maxima model creation
                }
                for (; this.dsmPendingFreeIndexY < dsmWriteCompletedIndexY; ++this.dsmPendingFreeIndexY)
                {
                    this.Dsm.SetRowToNull(this.dsmPendingFreeIndexY);
                }

                this.CellsWritten += dsmTileWritten.Cells;
                ++this.TilesWritten;
            }

            public bool TryGetNextTileWriteIndex(out int tileWriteIndexX, out int tileWriteIndexY, [NotNullWhen(true)] out DigitalSurfaceModel? dsmTileToWrite)
            {
                Debug.Assert((this.Las.SizeX == this.Dsm.VirtualRasterSizeInTilesX) && (this.Las.SizeY == this.Dsm.VirtualRasterSizeInTilesY));

                // scan to see if creation of one or more rows of DSM tiles has been completed since the last call
                // Similar to code in OnTileWritten().
                for (; this.dsmPendingCreationIndexY < this.Dsm.VirtualRasterSizeInTilesY; ++this.dsmPendingCreationIndexY)
                {
                    bool dsmRowIncompletelyCreated = false; // unlikely, but maybe no tiles to create in row
                    for (int xIndex = 0; xIndex < this.Dsm.VirtualRasterSizeInTilesX; ++xIndex)
                    {
                        if (this.Las[xIndex, this.dsmPendingCreationIndexY] != null) // point cloud tile grid fully populated before point reads start
                        {
                            if (this.Dsm[xIndex, this.dsmPendingCreationIndexY] == null)
                            {
                                dsmRowIncompletelyCreated = true;
                                break;
                            }
                        }
                    }

                    if (dsmRowIncompletelyCreated)
                    {
                        break;
                    }
                }

                // see if a tile is available for write a row basis
                // In the single row case, tile writes can begin as soon as their +x neighbor's been created. This is not currently
                // handled. Neither are more complex cases where voids in the grid permit writes to start before the next row of DSM
                // tiles has completed loading.
                int dsmCreationCompletedIndexY = this.dsmPendingCreationIndexY; // if DSM is fully created then all its tiles can be written
                if (this.dsmPendingCreationIndexY < this.Dsm.VirtualRasterSizeInTilesY)
                {
                    --dsmCreationCompletedIndexY; // row n of DSM isn't fully created yet so canopy maxima models for row n - 1 cannot yet be generated without edge effects
                }
                for (; this.dsmPendingWriteIndexY < dsmCreationCompletedIndexY; ++this.dsmPendingWriteIndexY)
                {
                    for (; this.dsmPendingWriteIndexX < this.Dsm.VirtualRasterSizeInTilesX; ++this.dsmPendingWriteIndexX)
                    {
                        DigitalSurfaceModel? createdTileCandidate = this.Dsm[this.dsmPendingWriteIndexX, this.dsmPendingWriteIndexY];
                        if (createdTileCandidate != null)
                        {
                            tileWriteIndexX = this.dsmPendingWriteIndexX;
                            tileWriteIndexY = this.dsmPendingWriteIndexY;
                            dsmTileToWrite = createdTileCandidate;

                            // advance to next grid position
                            ++this.dsmPendingWriteIndexX;
                            if (this.dsmPendingWriteIndexX >= this.Dsm.VirtualRasterSizeInTilesX)
                            {
                                ++this.dsmPendingWriteIndexY;
                                this.dsmPendingWriteIndexX = 0;
                            }
                            return true;
                        }
                    }

                    this.dsmPendingWriteIndexX = 0; // reset to beginning of row, next iteration of loop will increment in y
                }

                tileWriteIndexX = -1;
                tileWriteIndexY = -1;
                dsmTileToWrite = null;
                return false;
            }
        }
    }
}
