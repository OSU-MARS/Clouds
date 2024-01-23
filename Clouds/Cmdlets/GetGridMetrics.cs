using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

namespace Mars.Clouds.Cmdlets
{
    /// <summary>
    /// Caclculate standard ABA (area based approach) point cloud metrics using a set of point cloud tiles and a raster defining ABA cells.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "GridMetrics")]
    public class GetGridMetrics : LasTileCmdlet
    {
        [Parameter(HelpMessage = "Band number of -AbaCells to use in defining grid metrics cells. Ones based, default is the first band.")]
        [ValidateRange(1, 1000)]
        public int AbaCellBand { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Raster with a band defining the grid over which ABA (area based approach) point cloud metrics are calculated. Metrics are not calculated for cells outside the point cloud or which have a no data value in the first band. The raster must be in the same CRS as the point cloud tiles specified by -Las.")]
        [ValidateNotNullOrEmpty]
        public string? AbaCells { get; set; }

        public GetGridMetrics()
        {
            this.AbaCellBand = 1;
            // this.AbaCells is mandatory
        }

        protected override void ProcessRecord()
        {
            Debug.Assert(String.IsNullOrEmpty(this.AbaCells) == false);

            using Dataset gridCellDefinitionDataset = Gdal.Open(this.AbaCells, Access.GA_ReadOnly);
            Raster abaCellDefinitions = Raster.Create(gridCellDefinitionDataset);
            int abaEpsg = abaCellDefinitions.Crs.ParseEpsg();

            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(abaEpsg);
            AbaGrid abaGrid = new(abaCellDefinitions, this.AbaCellBand - 1, lasGrid); // convert band number from ones based numbering to zero based indexing

            StandardMetricsRaster abaMetrics = new(abaGrid.Crs, abaGrid.Transform, abaGrid.XSize, abaGrid.YSize);
            BlockingCollection<List<PointListZirnc>> fullyPopulatedAbaCells = new(this.MaxTiles);

            Stopwatch stopwatch = new();
            stopwatch.Start();
            
            CancellationTokenSource cancellationTokenSource = new();
            int tilesLoaded = 0;
            Task readPoints = Task.Run(() => 
            {
                try
                {
                    for (int tileYindex = 0; tileYindex < lasGrid.YSize; ++tileYindex)
                    {
                        for (int tileXindex = 0; tileXindex < lasGrid.XSize; ++tileXindex)
                        {
                            LasTile? tile = lasGrid[tileXindex, tileYindex];
                            if ((tile == null) || (abaGrid.HasCellsInTile(tile) == false))
                            {
                                continue;
                            }

                            using LasReader pointReader = tile.CreatePointReader();
                            pointReader.ReadPointsToGrid(tile, abaGrid);

                            // check for cancellation before queing tile for metrics calculation
                            // Since tile loads are long, checking immediately before adding mitigates risk of queing blocking because
                            // the metrics task has faulted and the queue is full. (Locking could be used to remove the race condition
                            // entirely, but currently seems unnecessary as this appears to be an edge case.)
                            if (this.Stopping || cancellationTokenSource.IsCancellationRequested)
                            {
                                break;
                            }

                            abaGrid.QueueCompletedCells(tile, fullyPopulatedAbaCells);
                            ++tilesLoaded;

                            //FileInfo fileInfo = new(this.Las);
                            //UInt64 pointsRead = lasFile.Header.GetNumberOfPoints();
                            //float megabytesRead = fileInfo.Length / (1024.0F * 1024.0F);
                            //float gigabytesRead = megabytesRead / 1024.0F;
                            //double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                            //this.WriteVerbose("Gridded " + gigabytesRead.ToString("0.00") + " GB with " + pointsRead.ToString("#,0") + " points into " + abaGridCellsWithPoints + " grid cells in " + elapsedSeconds.ToString("0.000") + " s: " + (pointsRead / (1E6 * elapsedSeconds)).ToString("0.00") + " Mpoints/s, " + (megabytesRead / elapsedSeconds).ToString("0.0") + " MB/s.");
                        }

                        if (this.Stopping || cancellationTokenSource.IsCancellationRequested)
                        {
                            break;
                        }
                    }

                    // TODO: error checking to detect any incompletely loaded, and thus unqueued, ABA cells?
                }
                finally
                {
                    // ensure metrics calculation doesn't block indefinitely waiting for more data if an exception occurs during tile loading
                    fullyPopulatedAbaCells.CompleteAdding();
                }
            }, cancellationTokenSource.Token);

            int abaCellsCompleted = 0;
            Task calculateMetrics = Task.Run(() => 
            {
                SpatialReference crs = new(null);
                crs.ImportFromEPSG(abaEpsg);
                float crsLinearUnits = (float)crs.GetLinearUnits();
                float oneMeterHeightClass = 1.0F / crsLinearUnits;
                float twoMeterHeightThreshold = 2.0F / crsLinearUnits; // applied relative to mean ground height in each cell if ground points are classified, used as is if points aren't classified

                foreach (List<PointListZirnc> populatedAbaCellBatch in fullyPopulatedAbaCells.GetConsumingEnumerable())
                {
                    for (int batchIndex = 0; batchIndex < populatedAbaCellBatch.Count; ++batchIndex)
                    {
                        PointListZirnc abaCell = populatedAbaCellBatch[batchIndex];
                        try
                        {
                            abaCell.GetStandardMetrics(abaMetrics, oneMeterHeightClass, twoMeterHeightThreshold);
                            // cell's lists are no longer needed; release them so the memory can be used for continuing tile loading
                            // Since ClearAndRelease() zeros TilesLoaded it's called for consistency even if a cell contains no points.
                            abaCell.ClearAndRelease();
                            ++abaCellsCompleted;
                        }
                        catch (Exception exception)
                        {
                            throw new TaskCanceledException("Error calculating metrics for ABA cell with extent (" + abaCell.XMin + ", " + abaCell.XMax + ", " + abaCell.YMin + ", " + abaCell.YMax + ") at grid position (" + abaCell.XIndex + ", " + abaCell.YIndex + ").", exception, cancellationTokenSource.Token);
                        }

                        if (this.Stopping || cancellationTokenSource.IsCancellationRequested)
                        {
                            break;
                        }
                    }

                    if (this.Stopping || cancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }, cancellationTokenSource.Token);

            ProgressRecord gridMetricsProgress = new(0, "Get-GridMetrics", "Calculating metrics: " + abaCellsCompleted.ToString("#,#,0") + " of " + abaGrid.NonNullCells.ToString("#,0") + " cells (" + tilesLoaded + " of " + lasGrid.NonNullCells + " point cloud tiles)...");
            this.WriteProgress(gridMetricsProgress);

            TimeSpan progressUpdateInterval = TimeSpan.FromSeconds(10.0);
            Task[] gridMetricsTasks = [ readPoints, calculateMetrics ];
            while (Task.WaitAll(gridMetricsTasks, progressUpdateInterval) == false)
            {
                // unlike Task.WaitAll(Task[]), Task.WaitAll(Task[], TimeSpan) does not unblock and throw the exception if any task faults
                // If one task has faulted then cancellation is therefore desirable to stop the other tasks. If tile read faults metrics
                // calculation can see no more tiles will be added and can complete normally, leading to incomplete metrics in any grid cells
                // which weren't compeletely read. If metrics calculation faults the cmdlet will exit but, if not cancelled, tile read continues
                // until blocking indefinitely when maximum tile load is reached.
                if (readPoints.IsFaulted || calculateMetrics.IsFaulted)
                {
                    cancellationTokenSource.Cancel();
                }

                float fractionComplete = (float)abaCellsCompleted / (float)abaGrid.NonNullCells;
                gridMetricsProgress.StatusDescription = "Calculating metrics: " + abaCellsCompleted.ToString("#,#,0") + " of " + abaGrid.NonNullCells.ToString("#,0") + " cells (" + tilesLoaded + " of " + lasGrid.NonNullCells + " point cloud tiles)...";
                gridMetricsProgress.PercentComplete = (int)(100.0F * fractionComplete);
                gridMetricsProgress.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                this.WriteProgress(gridMetricsProgress);
            }

            this.WriteObject(abaMetrics);
            stopwatch.Stop();

            string elapsedTimeFormat = stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Calculated metrics for " + abaCellsCompleted.ToString("#,#,0") + " cells from " + tilesLoaded + " tiles in " + stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + (abaCellsCompleted / stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " cells/s.");
            base.ProcessRecord();
        }
    }
}
