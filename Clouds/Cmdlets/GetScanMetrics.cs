using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using Mars.Clouds.Segmentation;
using OSGeo.GDAL;
using System;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "ScanMetrics")]
    public class GetScanMetrics : LasTilesToRasterCmdlet
    {
        private TileRead? tileRead;

        public GetScanMetrics()
        {
            this.tileRead = null;
        }

        protected override void ProcessRecord()
        {
            if (String.IsNullOrWhiteSpace(this.Cells))
            {
                throw new NotSupportedException("-Cells is currently a mandatory parameter.");
            }

            using Dataset gridCellDefinitionDataset = Gdal.Open(this.Cells, Access.GA_ReadOnly);
            Raster cellDefinitions = Raster.Create(this.Cells, gridCellDefinitionDataset, readData: false);
            gridCellDefinitionDataset.FlushCache();

            const string cmdletName = "Get-ScanMetrics";
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName);
            if (SpatialReferenceExtensions.IsSameCrs(cellDefinitions.Crs, lasGrid.Crs) == false)
            {
                throw new NotSupportedException("Cell definition raster '" + this.Cells + "' is not in the same coordinate system ('" + cellDefinitions.Crs.GetName() + "') as the input point clouds ('" + lasGrid.Crs.GetName() + "').");
            }

            ScanMetricsRaster scanMetrics = new(cellDefinitions);

            // TODO: multithreaded read
            this.tileRead = new();
            ParallelTasks readPoints = new(1, () =>
            {
                byte[]? pointReadBuffer = null;
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
                        pointReader.ReadPointsToGrid(tile, scanMetrics, ref pointReadBuffer);
                        this.tileRead.IncrementTilesReadThreadSafe();

                        // check for cancellation before queing tile for metrics calculation
                        // Since tile loads are long, checking immediately before adding mitigates risk of queing blocking because
                        // the metrics task has faulted and the queue is full. (Locking could be used to remove the race condition
                        // entirely, but currently seems unnecessary as this appears to be an edge case.)
                        if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                        {
                            return;
                        }
                    }
                }
            }, this.CancellationTokenSource);

            TimedProgressRecord scanMetricsProgress = new(cmdletName, "Loaded " + tileRead.TilesRead + " of " + lasGrid.NonNullCells + " point cloud tiles...");
            this.WriteProgress(scanMetricsProgress);
            while (readPoints.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                scanMetricsProgress.StatusDescription = "Loaded " + tileRead.TilesRead + " of " + lasGrid.NonNullCells + " point cloud tiles...";
                scanMetricsProgress.Update(tileRead.TilesRead, lasGrid.NonNullCells);
                this.WriteProgress(scanMetricsProgress);
            }

            scanMetrics.OnPointAdditionComplete();
            this.WriteObject(scanMetrics);
            
            scanMetricsProgress.Stopwatch.Stop();
            this.WriteVerbose("Calculated metrics for " + tileRead.TilesRead + " tiles in " + scanMetricsProgress.Stopwatch.ToElapsedString() + ".");
            base.ProcessRecord();
        }
    }
}