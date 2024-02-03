using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
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
    public class GetGridMetrics : LasTilesToRasterCmdlet
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
            this.MaxThreads = 2;
            this.Settings = new();
        }

        protected override void ProcessRecord()
        {
            Debug.Assert(String.IsNullOrEmpty(this.Cells) == false);

            if (this.MaxThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxThreads), "-" + nameof(this.MaxThreads) + " must be at least two.");
            }

            using Dataset gridCellDefinitionDataset = Gdal.Open(this.Cells, Access.GA_ReadOnly);
            Raster cellDefinitions = Raster.Create(gridCellDefinitionDataset);
            int gridEpsg = cellDefinitions.Crs.ParseEpsg();

            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(gridEpsg);
            GridMetricsPointLists metricsGrid = new(cellDefinitions, this.CellBand - 1, lasGrid); // convert band number from ones based numbering to zero based indexing

            GridMetricsRaster metricsRaster = new(metricsGrid.Crs, metricsGrid.Transform, metricsGrid.XSize, metricsGrid.YSize, this.Settings);

            MetricsTileRead metricsRead = new(gridEpsg, metricsGrid.NonNullCells, this.MaxTiles);
            
            Task readPoints = Task.Run(() => this.ReadTiles(lasGrid, metricsGrid, metricsRead), metricsRead.CancellationTokenSource.Token);
            Task calculateMetrics = Task.Run(() => this.WriteTiles(metricsRaster, metricsRead), metricsRead.CancellationTokenSource.Token);

            Task[] gridMetricsTasks = [ readPoints, calculateMetrics ];
            this.WaitForTasks("Get-GridMetrics", gridMetricsTasks, lasGrid, metricsRead);
            this.WriteObject(metricsRaster);
            metricsRead.Stopwatch.Stop();

            string elapsedTimeFormat = metricsRead.Stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Calculated metrics for " + metricsRead.RasterCellsCompleted.ToString("#,#,0") + " cells from " + metricsRead.TilesLoaded + " tiles in " + metricsRead.Stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + (metricsRead.RasterCellsCompleted / metricsRead.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " cells/s.");
            base.ProcessRecord();
        }

        private void ReadTiles(LasTileGrid lasGrid, GridMetricsPointLists metricsGrid, MetricsTileRead tileRead)
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
                        if (this.Stopping || tileRead.CancellationTokenSource.IsCancellationRequested)
                        {
                            break;
                        }

                        metricsGrid.QueueCompletedCells(tile, tileRead.FullyPopulatedCells);
                        ++tileRead.TilesLoaded;

                        //FileInfo fileInfo = new(this.Las);
                        //UInt64 pointsRead = lasFile.Header.GetNumberOfPoints();
                        //float megabytesRead = fileInfo.Length / (1024.0F * 1024.0F);
                        //float gigabytesRead = megabytesRead / 1024.0F;
                        //double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                        //this.WriteVerbose("Gridded " + gigabytesRead.ToString("0.00") + " GB with " + pointsRead.ToString("#,0") + " points into " + abaGridCellsWithPoints + " grid cells in " + elapsedSeconds.ToString("0.000") + " s: " + (pointsRead / (1E6 * elapsedSeconds)).ToString("0.00") + " Mpoints/s, " + (megabytesRead / elapsedSeconds).ToString("0.0") + " MB/s.");
                    }

                    if (this.Stopping || tileRead.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }

                // TODO: error checking to detect any incompletely loaded, and thus unqueued, ABA cells?
            }
            finally
            {
                // ensure metrics calculation doesn't block indefinitely waiting for more data if an exception occurs during tile loading
                tileRead.FullyPopulatedCells.CompleteAdding();
            }
        }
        private void WriteTiles(GridMetricsRaster metricsRaster, MetricsTileRead tileRead)
        {
            float crsLinearUnits = (float)tileRead.Crs.GetLinearUnits();
            float oneMeterHeightClass = 1.0F / crsLinearUnits;
            float twoMeterHeightThreshold = 2.0F / crsLinearUnits; // applied relative to mean ground height in each cell if ground points are classified, used as is if points aren't classified

            foreach (List<PointListZirnc> populatedCellBatch in tileRead.FullyPopulatedCells.GetConsumingEnumerable())
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
                        ++tileRead.RasterCellsCompleted;
                    }
                    catch (Exception exception)
                    {
                        throw new TaskCanceledException("Error calculating metrics for ABA cell with extent (" + gridCell.XMin + ", " + gridCell.XMax + ", " + gridCell.YMin + ", " + gridCell.YMax + ") at grid position (" + gridCell.XIndex + ", " + gridCell.YIndex + ").", exception, tileRead.CancellationTokenSource.Token);
                    }

                    if (this.Stopping || tileRead.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }

                if (this.Stopping || tileRead.CancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private class MetricsTileRead : TileReadToRaster
        {
            public SpatialReference Crs { get; private init; }
            public BlockingCollection<List<PointListZirnc>> FullyPopulatedCells { get; private init; }

            public MetricsTileRead(int gridEpsg, int metricsCells, int maxTiles) 
                : base(metricsCells)
            {
                this.Crs = new(null);
                this.Crs.ImportFromEPSG(gridEpsg);
                this.FullyPopulatedCells = new(maxTiles);
            }
        }
    }
}
