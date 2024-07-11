using Mars.Clouds.Las;
using System;
using System.Diagnostics;
using System.Management.Automation;
using System.IO;
using Mars.Clouds.Extensions;
using Mars.Clouds.Cmdlets.Hardware;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Orthoimages")]
    public class GetOrthoimages : LasTilesToTilesCmdlet
    {
        private TileReadWrite? imageReadWrite;

        [Parameter(HelpMessage = "Number of unsigned bits per cell in RGB, NIR, and intensity bands in output tiles. Can be 16, 32, or 64. Default is 16, which is likely to occasionally result in points' RGB, NIR, and possibly intensity values of 65535 being reduced to 65534 to disambiguate them from no data values.")]
        [ValidateRange(16, 64)] // could also use [ValidateSet] but string conversion is required
        public int BitDepth { get; set; }

        [Parameter(HelpMessage = "Size of an orthoimage pixel in the point clouds' CRS units. Must be an integer multiple of the tile size. Default is 0.5 m for metric point clouds and 1.5 feet for point clouds with English units.")]
        public double CellSize { get; set; }

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to write image to as a GeoTIFF or 2,3) path to a directory to write image tiles to.")]
        [ValidateNotNullOrEmpty]
        public string Image { get; set; }

        [Parameter(HelpMessage = "Enable use of unbuffered IO on Windows. This is likely to be helpful in utilizing PCIe 4.0 x4 and faster NVMe drives. Default is buffered IO.")]
        public SwitchParameter Unbuffered { get; set; }

        public GetOrthoimages() 
        {
            this.BitDepth = 16;
            this.CellSize = -1.0;
            this.Image = String.Empty;
            this.Unbuffered = false;
            this.NoWrite = false;
        }

        protected override void ProcessRecord() 
        {
            if ((this.BitDepth != 16) && (this.BitDepth != 32) && (this.BitDepth != 64))
            {
                throw new ParameterOutOfRangeException(nameof(this.BitDepth), this.BitDepth + " bit depth is not supported. Bit depth must be 16, 32, or 64 bits per cell for RGB, NIR, and intensity bands.");
            }

            if (this.MaxThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxThreads), "-" + nameof(this.MaxThreads) + " must be at least two.");
            }

            string cmdletName = "Get-Orthoimages";
            bool imagePathIsDirectory = Directory.Exists(this.Image);
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, nameof(this.Image), imagePathIsDirectory);

            (int imageTileSizeX, int imageTileSizeY) = this.SetCellSize(lasGrid);
            this.imageReadWrite = new(imagePathIsDirectory);

            // start single reader and multiple writers
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetPointsToImageBandwidth(this.Unbuffered);
            int readThreads = this.GetLasTileReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs, minWorkerThreadsPerReadThread: 0);
            int maxUsefulThreads = Int32.Min((int)Single.Ceiling(1.5F * readThreads), lasGrid.NonNullCells);
            Debug.Assert(maxUsefulThreads > 0);

            long cellsWritten = 0;
            //ObjectPool<PointBatchXyirnRgbn> pointBatchPool = new();
            using SemaphoreSlim readSemaphore = new(initialCount: readThreads, maxCount: readThreads);
            ParallelTasks orthoimageTasks = new(Int32.Min(maxUsefulThreads, this.MaxThreads), () =>
            {
                ImageRaster<UInt64>? imageTile = null;
                byte[]? pointReadBuffer = null;
                readSemaphore.Wait(this.imageReadWrite.CancellationTokenSource.Token);
                if (this.Stopping || this.imageReadWrite.CancellationTokenSource.IsCancellationRequested)
                {
                    return; // task is cancelled so point in looking for a tile to read
                }

                for (int tileIndex = this.imageReadWrite.GetNextTileReadIndexThreadSafe(); tileIndex < lasGrid.Cells; tileIndex = this.imageReadWrite.GetNextTileReadIndexThreadSafe())
                {
                    LasTile? lasTile = lasGrid[tileIndex];
                    if (lasTile == null)
                    {
                        continue; // nothing to do as no tile is present at this grid position
                    }

                    // read tile
                    imageTile = ImageRaster<UInt64>.CreateRecreateOrReset(imageTile, lasGrid.Crs, lasTile, this.CellSize, imageTileSizeX, imageTileSizeY);
                    using LasReader pointReader = lasTile.CreatePointReader(unbuffered: this.Unbuffered, enableAsync: false);
                    pointReader.ReadPointsToImage(lasTile, imageTile, ref pointReadBuffer);
                    //PointList<PointBatchXyirnRgbn> tilePoints = pointReader.ReadPoints(lasTile, pointBatchPool);
                    readSemaphore.Release();
                    this.imageReadWrite.IncrementTilesReadThreadSafe();

                    if (this.Stopping || this.imageReadWrite.CancellationTokenSource.IsCancellationRequested)
                    {
                        return; // if cmdlet's been stopped with ctrl+c the read semaphore may already be disposed
                    }

                    // calculate means and set no data values
                    //imageTile.Add(lasTile, tilePoints);
                    //tilePoints.ReturnThreadSafe(pointBatchPool);
                    imageTile.OnPointAdditionComplete();
                    if (this.Stopping || this.imageReadWrite.CancellationTokenSource.IsCancellationRequested)
                    {
                        return; // no point in writing tile
                    }

                    // write tile to disk
                    if (this.NoWrite == false)
                    {
                        string tileName = Tile.GetName(lasTile.FilePath);
                        string imageTilePath = this.imageReadWrite.OutputPathIsDirectory ? Path.Combine(this.Image, tileName + Constant.File.GeoTiffExtension) : this.Image;
                        imageTile.Write(imageTilePath, this.BitDepth, this.CompressRasters);

                        lock (this.imageReadWrite)
                        {
                            cellsWritten += imageTile.Cells;
                            ++this.imageReadWrite.TilesWritten;
                        }
                    }

                    // reacquire read semaphore for next tile
                    // Unnecessary on last loop iteration but no good way to identify if this is the last iteration.
                    readSemaphore.Wait(this.imageReadWrite.CancellationTokenSource.Token);
                }

                // ensure read semaphore is released
                // Reads have been initiated on all tiles at this point but it's probable there's one or more threads blocked in Wait() which
                // must acquire the semaphore to exit.
                readSemaphore.Release();

                // not necessary but may help the GC
                //pointBatchPool.Clear();
            }, this.imageReadWrite.CancellationTokenSource);

            int activeReadThreads = readThreads - readSemaphore.CurrentCount;
            TimedProgressRecord progress = new(cmdletName, imageReadWrite.GetLasReadTileWriteStatusDescription(lasGrid, activeReadThreads, orthoimageTasks.Count));
            this.WriteProgress(progress);
            while (orthoimageTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                activeReadThreads = readThreads - readSemaphore.CurrentCount;
                progress.StatusDescription = imageReadWrite.GetLasReadTileWriteStatusDescription(lasGrid, activeReadThreads, orthoimageTasks.Count);
                progress.Update(this.imageReadWrite.TilesWritten, lasGrid.NonNullCells);
                this.WriteProgress(progress);
            }

            progress.Stopwatch.Stop();
            this.WriteVerbose("Found brightnesses of " + cellsWritten.ToString("n0") + " pixels in " + this.imageReadWrite.TilesRead + (this.imageReadWrite.TilesRead == 1 ? " point cloud tile in " : " point cloud tiles in ") + progress.Stopwatch.ToElapsedString() + ": " + (this.imageReadWrite.TilesWritten / progress.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " tiles/s.");
            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            this.imageReadWrite?.CancellationTokenSource.Cancel();
            base.StopProcessing();
        }

        private (int tileSizeX, int tileSizeY) SetCellSize(LasTileGrid lasGrid)
        {
            if (this.CellSize < 0.0)
            {
                double crsLinearUnits = lasGrid.Crs.GetLinearUnits();
                this.CellSize = crsLinearUnits == 1.0 ? 0.5 : 1.5; // 0.5 m or 1.5 feet
            }

            int outputTileSizeX = (int)(lasGrid.Transform.CellWidth / this.CellSize);
            if (lasGrid.Transform.CellWidth - outputTileSizeX * this.CellSize != 0.0)
            {
                string units = lasGrid.Crs.GetLinearUnitsName();
                throw new ParameterOutOfRangeException(nameof(this.CellSize), "Point cloud tile grid pitch of " + lasGrid.Transform.CellWidth + " x " + lasGrid.Transform.CellHeight + " is not an integer multiple of the " + this.CellSize + " " + units + " output cell size.");
            }
            int outputTileSizeY = (int)(Double.Abs(lasGrid.Transform.CellHeight) / this.CellSize);
            if (Double.Abs(lasGrid.Transform.CellHeight) - outputTileSizeY * this.CellSize != 0.0)
            {
                string units = lasGrid.Crs.GetLinearUnitsName();
                throw new ParameterOutOfRangeException(nameof(this.CellSize), "Point cloud tile grid pitch of " + lasGrid.Transform.CellWidth + " x " + lasGrid.Transform.CellHeight + " is not an integer multiple of the " + this.CellSize + " " + units + " output cell size.");
            }

            return (outputTileSizeX, outputTileSizeY);
        }
    }
}
