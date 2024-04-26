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
        [Parameter(HelpMessage = "Name of band in -Cells to use in defining grid cells. Default is the first band.")]
        [ValidateNotNullOrWhiteSpace]
        public string? CellBand { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "1) path to a single digital terrain model (DTM) raster to estimate DSM height above ground from or 2,3) path to a directory containing DTM tiles whose file names match the DSM tiles. Each DSM must be a  single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrWhiteSpace]
        public string? Dtm { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating mean ground elevations. Default is null, which accepts any single band raster.")]
        public string? DtmBand { get; set; }

        [Parameter(HelpMessage = "Settings controlling which grid metrics the output rasters contain.")]
        [ValidateNotNull]
        public GridMetricsSettings Settings { get; set; }

        public GetGridMetrics()
        {
            this.CellBand = null;
            // this.Dtm is mandatory
            this.DtmBand = null;
            // leave this.MaxThreads at default for DTM read
            this.Settings = new();
        }

        protected override void ProcessRecord()
        {
            Debug.Assert((String.IsNullOrEmpty(this.Cells) == false) && (String.IsNullOrEmpty(this.Dtm) == false));
            if (this.MaxThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxThreads), "-" + nameof(this.MaxThreads) + " must be at least two.");
            }

            using Dataset gridCellDefinitionDataset = Gdal.Open(this.Cells, Access.GA_ReadOnly);
            Raster cellDefinitions = Raster.Read(gridCellDefinitionDataset);
            int gridEpsg = cellDefinitions.Crs.ParseEpsg();

            string cmdletName = "Get-GridMetrics";
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, gridEpsg);
            VirtualRaster<Raster<float>> dtm = this.ReadVirtualRaster<Raster<float>>(cmdletName, this.Dtm);
            if (SpatialReferenceExtensions.IsSameCrs(lasGrid.Crs, dtm.Crs) == false)
            {
                throw new NotSupportedException("The point clouds and DTM are currently required to be in the same CRS. The point cloud CRS is '" + lasGrid.Crs.GetName() + "' while the DTM CRS is " + dtm.Crs.GetName() + ".");
            }

            RasterBand cellMask = cellDefinitions.GetBand(this.CellBand);
            GridMetricsPointLists metricsGrid = new(cellMask, lasGrid); // convert band number from ones based numbering to zero based indexing
            GridMetricsRaster metricsRaster = new(metricsGrid, this.Settings);

            MetricsTileRead metricsRead = new(dtm, gridEpsg, metricsGrid.NonNullCells, this.MaxTiles);
            Task[] gridMetricsTasks = new Task[2];
            int readThreads = gridMetricsTasks.Length / 2;
            for (int readThread = 0; readThread < readThreads; ++readThread)
            {
                gridMetricsTasks[readThread] = Task.Run(() => this.ReadLasTiles(lasGrid, metricsGrid, metricsRead), metricsRead.CancellationTokenSource.Token);
            }
            for (int calculateThread = readThreads; calculateThread < gridMetricsTasks.Length; ++calculateThread)
            {
                gridMetricsTasks[calculateThread] = Task.Run(() => this.WriteTiles(metricsRaster, metricsRead), metricsRead.CancellationTokenSource.Token);
            }

            this.WaitForTasks("Get-GridMetrics", gridMetricsTasks, lasGrid, metricsRead);
            this.WriteObject(metricsRaster);
            metricsRead.Stopwatch.Stop();

            string elapsedTimeFormat = metricsRead.Stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Calculated metrics for " + metricsRead.RasterCellsCompleted.ToString("n0") + " cells from " + metricsRead.TilesLoaded + " tiles in " + metricsRead.Stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + (metricsRead.RasterCellsCompleted / metricsRead.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " cells/s.");
            base.ProcessRecord();
        }

        private void ReadLasTiles(LasTileGrid lasGrid, GridMetricsPointLists metricsGrid, MetricsTileRead tileRead)
        {
            try
            {
                for (int tileYindex = 0; tileYindex < lasGrid.SizeY; ++tileYindex)
                {
                    for (int tileXindex = 0; tileXindex < lasGrid.SizeX; ++tileXindex)
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
                    (double cellCenterX, double cellCenterY) = metricsRaster.Transform.GetCellCenter(gridCell.XIndex, gridCell.YIndex);
                    if (tileRead.DtmTiles.TryGetNeighborhood8(cellCenterX, cellCenterY, this.DtmBand, out VirtualRasterNeighborhood8<float>? dtmNeighborhood) == false)
                    {
                        throw new InvalidOperationException("Could not find DTM tile for metrics grid cell at (" + cellCenterX + ", " + cellCenterY + ") (metrics grid indices " + gridCell.XIndex + ", " + gridCell.YIndex + ") in DTM band " + this.DtmBand + ".");
                    }
                    try
                    {
                        metricsRaster.SetMetrics(gridCell, dtmNeighborhood, oneMeterHeightClass, twoMeterHeightThreshold);
                        // cell's lists are no longer needed; release them so the memory can be used for continuing tile loading
                        // Since ClearAndRelease() zeros TilesLoaded it's called for consistency even if a cell contains no points.
                        gridCell.ClearAndRelease(); // could use a PointListZirnc object pool
                        ++tileRead.RasterCellsCompleted;
                    }
                    catch (Exception exception)
                    {
                        throw new TaskCanceledException("Error calculating metrics for ABA cell with extent (" + gridCell.PointXMin + ", " + gridCell.PointXMax + ", " + gridCell.PointYMin + ", " + gridCell.PointYMax + ") at grid position (" + gridCell.XIndex + ", " + gridCell.YIndex + ").", exception, tileRead.CancellationTokenSource.Token);
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
            public VirtualRaster<Raster<float>> DtmTiles { get; private init; }
            public BlockingCollection<List<PointListZirnc>> FullyPopulatedCells { get; private init; }

            public MetricsTileRead(VirtualRaster<Raster<float>> dtmTiles, int gridEpsg, int metricsCells, int maxSimultaneouslyLoadedTiles) 
                : base(metricsCells)
            {
                this.Crs = new(null);
                this.Crs.ImportFromEPSG(gridEpsg);
                this.DtmTiles = dtmTiles;
                this.FullyPopulatedCells = new(maxSimultaneouslyLoadedTiles);
            }
        }
    }
}
