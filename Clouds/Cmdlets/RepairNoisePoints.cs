using Mars.Clouds.Cmdlets.Drives;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsDiagnostic.Repair, "NoisePoints")]
    public class RepairNoisePoints : LasTilesCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "Path to a directory containing DTM tiles whose file names match the point cloud tiles. Each DTM must be a single precision floating point raster with ground surface heights in the same CRS as the point cloud tiles.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dtm { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating mean ground elevations. Default is the first band.")]
        [ValidateNotNullOrWhiteSpace]
        public string? DtmBand { get; set; }

        [Parameter(HelpMessage = "Elevation threshold, in point cloud CRS units, above which points are classified as high noise. Default is 100 m for point clouds with metric units or 400 feet for point clouds with English units.")]
        [ValidateRange(0.0F, 30000.0F)] // upper bound: Chomolungma/Sagarmatha (Everest) in feet
        public float HighNoise { get; set; }

        [Parameter(HelpMessage = "Elevation threshold, in point cloud CRS units, below which points are classified as low noise. Default is -30 m for point clouds with metric units or -100 feet for point clouds with English units.")]
        [ValidateRange(-36000.0F, 0.0F)] // lower bound: Challenger Deep in feet
        public float LowNoise { get; set; }

        public RepairNoisePoints()
        {
            this.Dtm = String.Empty;
            this.DtmBand = null;
            this.HighNoise = Single.NaN;
            this.LowNoise = Single.NaN;
        }

        private int MaybeReclassifyPoints(LasTile lasTile, RepairNoiseReadWrite lasTileRead)
        {
            string dtmTilePath = LasTilesCmdlet.GetRasterTilePath(this.Dtm, Tile.GetName(lasTile.FilePath));
            RasterBand<float> dtmTile = RasterBand<float>.Read(dtmTilePath, this.DtmBand);
            using LasReaderWriter pointReaderWriter = lasTile.CreatePointReaderWriter();
            return pointReaderWriter.TryFindUnclassifiedNoise(lasTile, dtmTile, this.HighNoise, this.LowNoise);
        }

        protected override void ProcessRecord()
        {
            if (this.MaxThreads > this.MaxPointTiles)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxPointTiles), "-" + nameof(this.MaxPointTiles) + " must be greater than or equal to the maximum number of threads (" + this.MaxThreads + ") as each thread requires a tile to work with.");
            }

            string cmdletName = "Repair-NoisePoints";
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, requiredEpsg: null);
            double lasGridUnits = lasGrid.Crs.GetLinearUnits();
            if (Single.IsNaN(this.HighNoise))
            {
                this.HighNoise = lasGridUnits == 1.0 ? 100.0F : 400.0F;
            }
            if (Single.IsNaN(this.LowNoise))
            {
                this.LowNoise = lasGridUnits == 1.0 ? -30.0F : -100.0F;
            }

            // spin up point cloud read and tile worker threads
            DriveCapabilities driveCapabilities = DriveCapabilities.Create(this.Las);
            int readThreads = Int32.Min(driveCapabilities.GetPracticalThreadCount(LasReaderWriter.RepairNoisePointsSpeedInGBs), this.MaxThreads);

            RepairNoiseReadWrite tileChecks = new();
            Task[] checkTasks = new Task[Int32.Min(readThreads, lasGrid.NonNullCells)];
            for (int readThread = 0; readThread < checkTasks.Length; ++readThread)
            {
                checkTasks[readThread] = Task.Run(() => this.CheckLasTiles(lasGrid, this.MaybeReclassifyPoints, tileChecks), tileChecks.CancellationTokenSource.Token);
            }

            TimedProgressRecord progress = this.WaitForCheckTasks(cmdletName, checkTasks, lasGrid, tileChecks);

            progress.Stopwatch.Stop();
            string elapsedTimeFormat = progress.Stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Checked " + lasGrid.NonNullCells + " tiles in " + progress.Stopwatch.Elapsed.ToString(elapsedTimeFormat) + ".");
            base.ProcessRecord();
        }

        private void CheckLasTiles(LasTileGrid lasGrid, Func<LasTile, RepairNoiseReadWrite, int> checkTile, RepairNoiseReadWrite tileChecks)
        {
            for (int tileIndex = tileChecks.GetNextTileReadIndexThreadSafe(); tileIndex < lasGrid.Cells; tileIndex = tileChecks.GetNextTileReadIndexThreadSafe())
            {
                LasTile? lasTile = lasGrid[tileIndex];
                if (lasTile == null)
                {
                    continue; // nothing to do as no tile is present at this grid position
                }

                tileChecks.OnTileCheckComplete(checkTile(lasTile, tileChecks));

                // check for cancellation before queing tile for metrics calculation
                // Since tile loads are long, checking immediately before adding mitigates risk of queing blocking because
                // the metrics task has faulted and the queue is full. (Locking could be used to remove the race condition
                // entirely, but currently seems unnecessary as this appears to be an edge case.)
                if (this.Stopping || tileChecks.CancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private TimedProgressRecord WaitForCheckTasks(string cmdletName, Task[] checkTasks, LasTileGrid lasGrid, RepairNoiseReadWrite tileChecks)
        {
            TimedProgressRecord tileCheckProgress = new(cmdletName, "Checking tiles: " + tileChecks.TilesRead + " of " + lasGrid.NonNullCells + " tiles, " + tileChecks.PointsReclassified + (tileChecks.PointsReclassified == 1 ? " point" : " points") + " reclassified...");
            this.WriteProgress(tileCheckProgress);

            while (Task.WaitAll(checkTasks, LasTilesCmdlet.ProgressUpdateInterval) == false)
            {
                // see remarks in LasTilesToTilesCmdlet.WaitForTasks()
                for (int taskIndex = 0; taskIndex < checkTasks.Length; ++taskIndex)
                {
                    Task task = checkTasks[taskIndex];
                    if (task.IsFaulted)
                    {
                        tileChecks.CancellationTokenSource.Cancel();
                        throw task.Exception;
                    }
                }

                tileCheckProgress.StatusDescription = "Checking tiles: " + tileChecks.TilesRead + " of " + lasGrid.NonNullCells + " tiles, " + tileChecks.PointsReclassified + (tileChecks.PointsReclassified == 1 ? " point" : " points") + " reclassified...";
                tileCheckProgress.Update(tileChecks.TilesRead, lasGrid.NonNullCells);
                this.WriteProgress(tileCheckProgress);
            }

            return tileCheckProgress;
        }

        private class RepairNoiseReadWrite : TileRead
        {
            private int pointsReclassified;

            public RepairNoiseReadWrite()
            {
                this.pointsReclassified = 0;
            }

            public int PointsReclassified 
            { 
                get { return this.pointsReclassified; }
            }

            public void OnTileCheckComplete(int pointsReclassified)
            {
                Interlocked.Add(ref this.pointsReclassified, pointsReclassified);
            }
        }
    }
}
