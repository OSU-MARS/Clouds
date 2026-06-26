using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsDiagnostic.Repair, "NoisePoints")]
    public class RepairNoisePoints : LasFilesCmdlet
    {
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
            this.tileChecks = null;

            this.Dtm = String.Empty;
            this.DtmBand = null;
            this.HighNoise = Single.NaN;
            this.LowNoise = Single.NaN;
        }

        private void CheckLasTiles(List<string> cloudPaths, Func<string, RepairNoiseReadWrite, int> maybeReclassifyPoints, RepairNoiseReadWrite pointCloudReadWrite)
        {
            for (int fileIndex = pointCloudReadWrite.GetNextFileReadIndexThreadSafe(); fileIndex < cloudPaths.Count; fileIndex = pointCloudReadWrite.GetNextFileReadIndexThreadSafe())
            {
                pointCloudReadWrite.OnTileCheckComplete(maybeReclassifyPoints(cloudPaths[fileIndex], pointCloudReadWrite));

                // check for cancellation before queing tile for metrics calculation
                // Since tile loads are long, checking immediately before adding mitigates risk of queing blocking because
                // the metrics task has faulted and the queue is full. (Locking could be used to remove the race condition
                // entirely, but currently seems unnecessary as this appears to be an edge case.)
                if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        public override string GetName()
        {
            return $"{VerbsDiagnostic.Repair}-NoisePoints";
        }

        private int MaybeReclassifyPoints(string cloudPath, RepairNoiseReadWrite pointCloudReadWrite)
        {
            string dtmTilePath = GdalCmdlet.GetRasterTilePath(this.Dtm, Tile.GetName(cloudPath));
            RasterBand<float> dtm = RasterBand<float>.Read(dtmTilePath, this.DtmBand);

            using LasReader readerWriter = LasReader.CreateForPointReadAndWrite(cloudPath, this.DiscardOverrunningVlrs);
            LasFile lasFile = new(cloudPath, readerWriter, this.FallbackDate);

            SpatialReference poinCloudCrs = lasFile.GetSpatialReference();
            if (SpatialReferenceExtensions.IsSameCrs(poinCloudCrs, dtm.Crs) == false)
            {
                throw new NotSupportedException($"The point clouds and DTM are currently required to be in the same CRS. The point cloud CRS is '{poinCloudCrs.GetName()}' for '{cloudPath}' while the DTM CRS is {dtm.Crs.GetName()} for '{dtmTilePath}'.");
            }

            // marks points as noise but (currently) does not offer the option of removing them from the file
            // Since no points are removed the header's bounding box does not need to be updated. If needed, an integrated repair and
            // remove flow can be created.
            LasWriter writer = readerWriter.AsWriter();

            double lasGridProjectedLinearUnitInM = poinCloudCrs.GetProjectedLinearUnitInM();
            float highNoiseThreshold = Single.IsNaN(this.HighNoise) ? (lasGridProjectedLinearUnitInM == 1.0 ? 100.0F : 400.0F) : this.HighNoise;
            float lowNoiseThreshold = Single.IsNaN(this.LowNoise) ? (lasGridProjectedLinearUnitInM == 1.0 ? -30.0F : -100.0F) : this.LowNoise;

            int pointsReclassified = writer.TryFindUnclassifiedNoise(lasFile, dtm, highNoiseThreshold, lowNoiseThreshold);
            return pointsReclassified;
        }

        protected override void ProcessRecord()
        {
            List<string> lasFilePaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);

            // spin up point cloud read and tile worker threads
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasWriter.GetFindUnclassifiedNoiseBandwidth();
            int readThreads = Int32.Min(this.GetPointCloudReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs), lasFilePaths.Count);

            this.tileChecks = new();
            ParallelTasks checkTasks = new(readThreads, () =>
            {
                this.CheckLasTiles(lasFilePaths, this.MaybeReclassifyPoints, this.tileChecks);
            }, this.CancellationTokenSource);

            TimedProgressRecord progress = new(this.GetName(), $"{this.tileChecks.FilesRead} of {lasFilePaths.Count} tiles checked, {this.tileChecks.PointsReclassified + (this.tileChecks.PointsReclassified == 1 ? " point" : " points")} reclassified...");
            this.WriteProgress(progress);
            while (checkTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = $"{this.tileChecks.FilesRead} of {lasFilePaths.Count} tiles checked, {this.tileChecks.PointsReclassified + (this.tileChecks.PointsReclassified == 1 ? " point" : " points")} reclassified...";
                progress.Update(this.tileChecks.FilesRead, lasFilePaths.Count);
                this.WriteProgress(progress);
            }

            progress.Stopwatch.Stop();
            this.WriteVerbose($"Checked {lasFilePaths.Count} tiles in {progress.Stopwatch.ToElapsedString()}.");
            base.ProcessRecord();
        }

        private class RepairNoiseReadWrite : FileRead
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
