using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.GDAL;
using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Threading.Tasks;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "ScanMetrics")]
    public class GetScanMetrics : LasTileGridCmdlet
    {
        // TODO: LasTileCmdlet.MaxTiles is unused

        protected override void ProcessRecord()
        {
            Debug.Assert(String.IsNullOrEmpty(this.Cells) == false);

            using Dataset gridCellDefinitionDataset = Gdal.Open(this.Cells, Access.GA_ReadOnly);
            Raster cellDefinitions = Raster.Create(gridCellDefinitionDataset);

            int gridEpsg = cellDefinitions.Crs.ParseEpsg();
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(gridEpsg);
            Stopwatch stopwatch = new();
            stopwatch.Start();

            CancellationTokenSource cancellationTokenSource = new();
            ScanMetricsRaster scanMetrics = new(cellDefinitions);
            Extent scanMetricsExtent = new(scanMetrics);
            int tilesLoaded = 0;
            Task readPoints = Task.Run(() =>
            {
                for (int tileYindex = 0; tileYindex < lasGrid.YSize; ++tileYindex)
                {
                    for (int tileXindex = 0; tileXindex < lasGrid.XSize; ++tileXindex)
                    {
                        LasTile? tile = lasGrid[tileXindex, tileYindex];
                        if ((tile == null) || (scanMetricsExtent.Intersects(tile.GridExtent) == false))
                        {
                            continue;
                        }

                        using LasReader pointReader = tile.CreatePointReader();
                        pointReader.ReadPointsToGrid(tile, scanMetrics);

                        // check for cancellation before queing tile for metrics calculation
                        // Since tile loads are long, checking immediately before adding mitigates risk of queing blocking because
                        // the metrics task has faulted and the queue is full. (Locking could be used to remove the race condition
                        // entirely, but currently seems unnecessary as this appears to be an edge case.)
                        if (this.Stopping || cancellationTokenSource.IsCancellationRequested)
                        {
                            break;
                        }

                        ++tilesLoaded;
                    }

                    if (this.Stopping || cancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }, cancellationTokenSource.Token);

            ProgressRecord gridMetricsProgress = new(0, "Get-ScanMetrics", "Loaded " + tilesLoaded + " of " + lasGrid.NonNullCells + " point cloud tiles...");
            this.WriteProgress(gridMetricsProgress);

            TimeSpan progressUpdateInterval = TimeSpan.FromSeconds(10.0);
            while (readPoints.Wait(progressUpdateInterval) == false)
            {
                float fractionComplete = (float)tilesLoaded / (float)lasGrid.NonNullCells;
                gridMetricsProgress.StatusDescription = "Loaded " + tilesLoaded + " of " + lasGrid.NonNullCells + " point cloud tiles...";
                gridMetricsProgress.PercentComplete = (int)(100.0F * fractionComplete);
                gridMetricsProgress.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                this.WriteProgress(gridMetricsProgress);
            }


            scanMetrics.OnPointAdditionComplete();
            this.WriteObject(scanMetrics);
            stopwatch.Stop();

            string elapsedTimeFormat = stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Calculated metrics for " + tilesLoaded + " tiles in " + stopwatch.Elapsed.ToString(elapsedTimeFormat) + ".");
            base.ProcessRecord();
        }
    }
}