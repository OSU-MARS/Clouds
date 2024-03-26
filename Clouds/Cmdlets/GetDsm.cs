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
        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to write DSM to as a GeoTIFF or 2,3) path to a directory to write DSM tiles to.")]
        [ValidateNotNullOrWhiteSpace]
        public string? Dsm { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "1) path to a single digital terrain model (DTM) raster to estimate DSM height above ground from or 2,3) path to a directory containing DTM tiles whose file names match the DSM tiles. Each DSM must be a  single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrWhiteSpace]
        public string? Dtm { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating mean ground elevations. Default is the first band.")]
        [ValidateNotNullOrWhiteSpace]
        public string? DtmBand { get; set; }

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
            this.LayerSeparation = -1.0F;
            // leave this.MaxThreads at default for DTM read
            this.ReadThreads = 1;
        }

        private void CreateAndWriteTiles(DsmReadCreateWrite dsmReadCreateWrite)
        {
            Debug.Assert(this.Dsm != null);
            const int dsmCreationTimeoutInMs = 1000;

            // create and write tiles
            // Loop continues for as long as there is new work to begin. This structure allows worker threads to exit after writing the
            // last tile available to them. TryTake() returns immediately once adding is completed so, with the current code, if threads
            // didn't exit they'd spin on TryGetNextTileWriteIndex() until the last tile write completed.
            while (dsmReadCreateWrite.TileWritesInitiated < dsmReadCreateWrite.Las.NonNullCells)
            {
                // create DSM tiles while additional 
                // MoveNext() blocks until all tiles have been read becaues LasTilesToTilesCmdlet.ReadTiles() doesn't call
                // CompleteAdding() until after the last read finishes.
                if (dsmReadCreateWrite.LoadedTiles.TryTake(out (string Name, Grid<PointListZs> PointZs) lasTile, dsmCreationTimeoutInMs, dsmReadCreateWrite.CancellationTokenSource.Token))
                {
                    try
                    {
                        (double tileCenterX, double tileCenterY) = lasTile.PointZs.GetCentroid();
                        if (dsmReadCreateWrite.Dtm.TryGetTileBand(tileCenterX, tileCenterY, this.DtmBand, out RasterBand<float>? dtmTile) == false)
                        {
                            throw new InvalidOperationException("DSM generation failed. Could not find underlying DTM tile at (" + tileCenterX + ", " + tileCenterY + ").");
                        }

                        // find mean ground elevation
                        for (int cellIndex = 0; cellIndex < lasTile.PointZs.Cells; ++cellIndex)
                        {
                            PointListZs? cellPoints = lasTile.PointZs[cellIndex];
                            cellPoints?.OnPointAdditionComplete();
                        }

                        // create DSM tile
                        DigitalSurfaceModel dsmTileToAdd = new(lasTile.PointZs, dtmTile, this.LayerSeparation);
                        int dsmTileIndexX;
                        int dsmTileIndexY;
                        lock (dsmReadCreateWrite.Dsm)
                        {
                            (dsmTileIndexX, dsmTileIndexY) = dsmReadCreateWrite.Dsm.Add(lasTile.Name, dsmTileToAdd);
                        }

                        // release memory associated with LAS points
                        // TODO: tile object pool
                        for (int cellIndex = 0; cellIndex < lasTile.PointZs.Cells; ++cellIndex)
                        {
                            lasTile.PointZs[cellIndex] = null;
                        }
                    }
                    catch (Exception exception)
                    {
                        throw new TaskCanceledException("Failed to process tile '" + lasTile.Name + "' with extent (" + lasTile.PointZs.GetExtentString() + ").", exception, dsmReadCreateWrite.CancellationTokenSource.Token);
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
            (LasTileGrid lasGrid, int dsmTileSizeX, int dsmTileSizeY) = this.ReadLasHeadersAndCellSize(cmdletName, nameof(this.Dsm), dsmPathIsDirectory);
            // TODO: load only DTM tiles with corresponding LAS tiles
            VirtualRaster<Raster<float>> dtm = this.ReadVirtualRaster<Raster<float>>(cmdletName, this.Dtm);
            // TODO: check LAS and DTM tiles align
            if (SpatialReferenceExtensions.IsSameCrs(lasGrid.Crs, dtm.Crs) == false)
            {
                throw new NotSupportedException("The point clouds and DTM are currently required to be in the same CRS. The point cloud CRS is '" + lasGrid.Crs.GetName() + "' while the DTM CRS is " + dtm.Crs.GetName() + ".");
            }

            if (this.LayerSeparation < 0.0F)
            {
                double crsLinearUnits = lasGrid.Crs.GetLinearUnits();
                this.LayerSeparation = crsLinearUnits == 1.0 ? 3.0F : 10.0F; // 3 m or 10 feet
            }

            VirtualRaster<DigitalSurfaceModel> dsm = new(lasGrid, dtm);
            DsmReadCreateWrite dsmReadCreateWrite = new(lasGrid, dsm, dtm, this.MaxTiles, dsmPathIsDirectory);
            Task[] dsmTasks = new Task[Int32.Min(2 * this.ReadThreads, this.MaxThreads)];
            for (int readThread = 0; readThread < this.ReadThreads; ++readThread)
            {
                dsmTasks[readThread] = Task.Run(() => this.ReadLasTiles(lasGrid, this.ReadTile, dsmReadCreateWrite), dsmReadCreateWrite.CancellationTokenSource.Token);
            }
            for (int workerThread = this.ReadThreads; workerThread < dsmTasks.Length; ++workerThread)
            {
                dsmTasks[workerThread] = Task.Run(() => this.CreateAndWriteTiles(dsmReadCreateWrite), dsmReadCreateWrite.CancellationTokenSource.Token);
            }

            this.WaitForLasReadTileWriteTasks("Get-Dsm", dsmTasks, lasGrid, dsmReadCreateWrite);

            string elapsedTimeFormat = dsmReadCreateWrite.Stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Generated " + dsmReadCreateWrite.CellsWritten.ToString("#,#,#,0") + " DSM cells from " + lasGrid.NonNullCells + " point cloud tiles in " + dsmReadCreateWrite.Stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + (dsmReadCreateWrite.TilesWritten / dsmReadCreateWrite.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " tiles/s.");
            base.ProcessRecord();
        }

        private Grid<PointListZs> ReadTile(LasTile lasTile, DsmReadCreateWrite dsmReadCreateWrite)
        {
            GridGeoTransform lasTileTransform = new(lasTile.GridExtent, this.CellSize, this.CellSize);
            Grid<PointListZs> tilePointZ = new(lasTile.GetSpatialReference(), lasTileTransform, dsmReadCreateWrite.TileSizeX, dsmReadCreateWrite.TileSizeY);
            using LasReader pointReader = lasTile.CreatePointReader();
            pointReader.ReadPointsToGrid(lasTile, tilePointZ);

            return tilePointZ;
        }

        private class DsmReadCreateWrite : TileReadWrite<Grid<PointListZs>>
        {
            private readonly bool[,] dsmCompletedWriteMap;
            private int dsmPendingCreationIndexY;
            private int dsmPendingFreeIndexY;
            private int dsmPendingWriteCompletionIndexY;
            private int dsmPendingWriteIndexX;
            private int dsmPendingWriteIndexY;

            public VirtualRaster<DigitalSurfaceModel> Dsm { get; private init; }
            public VirtualRaster<Raster<float>> Dtm { get; private init; }
            public LasTileGrid Las { get; private init; }
            public int TileWritesInitiated { get; set; }

            public DsmReadCreateWrite(LasTileGrid lasGrid, VirtualRaster<DigitalSurfaceModel> dsm, VirtualRaster<Raster<float>> dtm, int maxSimultaneouslyLoadedTiles, bool dsmPathIsDirectory)
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
                this.Dtm = dtm;
                this.Las = lasGrid;
                this.TileWritesInitiated = 0;
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
