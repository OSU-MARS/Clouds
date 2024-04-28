using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.GDAL;
using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Threading.Tasks;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "ScanMetrics")]
    public class GetScanMetrics : LasTilesToRasterCmdlet
    {
        // TODO: LasTileCmdlet.MaxTiles is unused

        public GetScanMetrics()
        {
            this.MaxThreads = 1;
        }

        protected override void ProcessRecord()
        {
            Debug.Assert(String.IsNullOrEmpty(this.Cells) == false);

            using Dataset gridCellDefinitionDataset = Gdal.Open(this.Cells, Access.GA_ReadOnly);
            Raster cellDefinitions = Raster.Read(gridCellDefinitionDataset, readData: true);

            string cmdletName = "Get-GridMetrics";
            int gridEpsg = cellDefinitions.Crs.ParseEpsg();
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, gridEpsg);

            TileRead tileRead = new();
            ScanMetricsRaster scanMetrics = new(cellDefinitions);
            Task readPoints = Task.Run(() => this.ReadTiles(lasGrid, scanMetrics, tileRead), tileRead.CancellationTokenSource.Token);

            ProgressRecord gridMetricsProgress = new(0, cmdletName, "Loaded " + tileRead.TilesLoaded + " of " + lasGrid.NonNullCells + " point cloud tiles...");
            this.WriteProgress(gridMetricsProgress);

            while (readPoints.Wait(LasTilesCmdlet.ProgressUpdateInterval) == false)
            {
                float fractionComplete = (float)tileRead.TilesLoaded / (float)lasGrid.NonNullCells;
                gridMetricsProgress.StatusDescription = "Loaded " + tileRead.TilesLoaded + " of " + lasGrid.NonNullCells + " point cloud tiles...";
                gridMetricsProgress.PercentComplete = (int)(100.0F * fractionComplete);
                gridMetricsProgress.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(tileRead.Stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                this.WriteProgress(gridMetricsProgress);
            }

            scanMetrics.OnPointAdditionComplete();
            this.WriteObject(scanMetrics);
            tileRead.Stopwatch.Stop();

            string elapsedTimeFormat = tileRead.Stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Calculated metrics for " + tileRead.TilesLoaded + " tiles in " + tileRead.Stopwatch.Elapsed.ToString(elapsedTimeFormat) + ".");
            base.ProcessRecord();
        }

        private void ReadTiles(LasTileGrid lasGrid, ScanMetricsRaster scanMetrics, TileRead tileRead)
        {
            Extent scanMetricsExtent = new(scanMetrics);
            for (int tileYindex = 0; tileYindex < lasGrid.SizeY; ++tileYindex)
            {
                for (int tileXindex = 0; tileXindex < lasGrid.SizeX; ++tileXindex)
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
                    if (this.Stopping || tileRead.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }

                    ++tileRead.TilesLoaded;
                }

                if (this.Stopping || tileRead.CancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}