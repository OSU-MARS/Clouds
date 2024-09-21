using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsDiagnostic.Repair, "NoisePoints")]
    public class RepairNoisePoints : LasTilesCmdlet
    {
        private readonly CancellationTokenSource cancellationTokenSource;
        private RepairNoiseReadWrite? tileChecks;

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
            this.cancellationTokenSource = new();
            this.tileChecks = null;

            this.Dtm = String.Empty;
            this.DtmBand = null;
            this.HighNoise = Single.NaN;
            this.LowNoise = Single.NaN;
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
                if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private int MaybeReclassifyPoints(LasTile lasTile, RepairNoiseReadWrite lasTileRead)
        {
            string dtmTilePath = LasTilesCmdlet.GetRasterTilePath(this.Dtm, Tile.GetName(lasTile.FilePath));
            RasterBand<float> dtmTile = RasterBand<float>.Read(dtmTilePath, this.DtmBand);

            // marks points as noise but (currently) does not offer the option of removing them from the file
            // Since no points are removed the header's bounding box does not need to be updated. If needed, an integrated repair and
            // remove flow can be created.
            using LasReaderWriter pointReaderWriter = lasTile.CreatePointReaderWriter();
            int pointsReclassified = pointReaderWriter.TryFindUnclassifiedNoise(lasTile, dtmTile, this.HighNoise, this.LowNoise);
            return pointsReclassified;
        }

        protected override void ProcessRecord()
        {
            const string cmdletName = "Repair-NoisePoints";
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
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReaderWriter.GetFindNoisePointsBandwidth();
            int availableReadThreads = this.GetLasTileReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs, minWorkerThreadsPerReadThread: 0);
            int checkThreads = Int32.Min(availableReadThreads, lasGrid.NonNullCells);

            this.tileChecks = new();
            ParallelTasks checkTasks = new(checkThreads, () =>
            {
                this.CheckLasTiles(lasGrid, this.MaybeReclassifyPoints, this.tileChecks);
            }, this.CancellationTokenSource);

            TimedProgressRecord progress = new(cmdletName, this.tileChecks.TilesRead + " of " + lasGrid.NonNullCells + " tiles checked, " + this.tileChecks.PointsReclassified + (this.tileChecks.PointsReclassified == 1 ? " point" : " points") + " reclassified...");
            this.WriteProgress(progress);
            while (checkTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = this.tileChecks.TilesRead + " of " + lasGrid.NonNullCells + " tiles checked, " + this.tileChecks.PointsReclassified + (this.tileChecks.PointsReclassified == 1 ? " point" : " points") + " reclassified...";
                progress.Update(this.tileChecks.TilesRead, lasGrid.NonNullCells);
                this.WriteProgress(progress);
            }

            progress.Stopwatch.Stop();
            this.WriteVerbose("Checked " + lasGrid.NonNullCells + " tiles in " + progress.Stopwatch.ToElapsedString() + ".");
            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            this.CancellationTokenSource.Cancel();
            base.StopProcessing();
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
