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
    /// Caclculate gridded point cloud metrics (for area based approaches) using a set of point cloud tiles and a raster defining the grid cells.
    /// </summary>
    /// <remarks>
    /// Primarily intended for the 20–40 m grid sizes commonly used with LiDAR or photogrammetric area based approaches to forest inventory. The
    /// code is cell size agnostic, however, and can also be used to get point cloud informatics at submeter resolutions or at larger scales.
    /// </remarks>
    [Cmdlet(VerbsCommon.Get, "GridMetrics")]
    public class GetGridMetrics : LasTileGridCmdlet
    {
        [Parameter(HelpMessage = "Band number of -Cells to use in defining grid cells. Ones based, default is the first band.")]
        [ValidateRange(1, 1000)]
        public int CellBand { get; set; }

        [Parameter(HelpMessage = "Settings controlling which grid metrics the output rasters contain.")]
        [ValidateNotNull]
        public GridMetricsSettings Settings { get; set; }

        public GetGridMetrics()
        {
            this.CellBand = 1;
            this.Settings = new();
        }

        protected override void ProcessRecord()
        {
            Debug.Assert(String.IsNullOrEmpty(this.Cells) == false);

            using Dataset gridCellDefinitionDataset = Gdal.Open(this.Cells, Access.GA_ReadOnly);
            Raster cellDefinitions = Raster.Create(gridCellDefinitionDataset);
            int gridEpsg = cellDefinitions.Crs.ParseEpsg();

            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(gridEpsg);
            GridMetricsPointLists metricsGrid = new(cellDefinitions, this.CellBand - 1, lasGrid); // convert band number from ones based numbering to zero based indexing

            GridMetricsRaster metricsRaster = new(metricsGrid.Crs, metricsGrid.Transform, metricsGrid.XSize, metricsGrid.YSize, this.Settings);
            BlockingCollection<List<PointListZirnc>> fullyPopulatedCells = new(this.MaxTiles);

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
                            if ((tile == null) || (metricsGrid.HasCellsInTile(tile) == false))
                            {
                                continue;
                            }

                            using LasReader pointReader = tile.CreatePointReader();
                            pointReader.ReadPointsToGrid(tile, metricsGrid);

                            // check for cancellation before queing tile for metrics calculation
                            // Since tile loads are long, checking immediately before adding mitigates risk of queing blocking because
                            // the metrics task has faulted and the queue is full. (Locking could be used to remove the race condition
                            // entirely, but currently seems unnecessary as this appears to be an edge case.)
                            if (this.Stopping || cancellationTokenSource.IsCancellationRequested)
                            {
                                break;
                            }

                            metricsGrid.QueueCompletedCells(tile, fullyPopulatedCells);
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
                    fullyPopulatedCells.CompleteAdding();
                }
            }, cancellationTokenSource.Token);

            int cellsCompleted = 0;
            Task calculateMetrics = Task.Run(() => 
            {
                SpatialReference crs = new(null);
                crs.ImportFromEPSG(gridEpsg);
                float crsLinearUnits = (float)crs.GetLinearUnits();
                float oneMeterHeightClass = 1.0F / crsLinearUnits;
                float twoMeterHeightThreshold = 2.0F / crsLinearUnits; // applied relative to mean ground height in each cell if ground points are classified, used as is if points aren't classified

                foreach (List<PointListZirnc> populatedCellBatch in fullyPopulatedCells.GetConsumingEnumerable())
                {
                    for (int batchIndex = 0; batchIndex < populatedCellBatch.Count; ++batchIndex)
                    {
                        PointListZirnc gridCell = populatedCellBatch[batchIndex];
                        try
                        {
                            metricsRaster.SetMetrics(gridCell, oneMeterHeightClass, twoMeterHeightThreshold);
                            // cell's lists are no longer needed; release them so the memory can be used for continuing tile loading
                            // Since ClearAndRelease() zeros TilesLoaded it's called for consistency even if a cell contains no points.
                            gridCell.ClearAndRelease();
                            ++cellsCompleted;
                        }
                        catch (Exception exception)
                        {
                            throw new TaskCanceledException("Error calculating metrics for ABA cell with extent (" + gridCell.XMin + ", " + gridCell.XMax + ", " + gridCell.YMin + ", " + gridCell.YMax + ") at grid position (" + gridCell.XIndex + ", " + gridCell.YIndex + ").", exception, cancellationTokenSource.Token);
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

            ProgressRecord gridMetricsProgress = new(0, "Get-GridMetrics", "Calculating metrics: " + cellsCompleted.ToString("#,#,0") + " of " + metricsGrid.NonNullCells.ToString("#,0") + " cells (" + tilesLoaded + " of " + lasGrid.NonNullCells + " point cloud tiles)...");
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

                float fractionComplete = (float)cellsCompleted / (float)metricsGrid.NonNullCells;
                gridMetricsProgress.StatusDescription = "Calculating metrics: " + cellsCompleted.ToString("#,#,0") + " of " + metricsGrid.NonNullCells.ToString("#,0") + " cells (" + tilesLoaded + " of " + lasGrid.NonNullCells + " point cloud tiles)...";
                gridMetricsProgress.PercentComplete = (int)(100.0F * fractionComplete);
                gridMetricsProgress.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                this.WriteProgress(gridMetricsProgress);
            }

            this.WriteObject(metricsRaster);
            stopwatch.Stop();

            string elapsedTimeFormat = stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Calculated metrics for " + cellsCompleted.ToString("#,#,0") + " cells from " + tilesLoaded + " tiles in " + stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + (cellsCompleted / stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " cells/s.");
            base.ProcessRecord();
        }
    }
}
