using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        public string? CellBand { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "1) path to a single digital terrain model (DTM) raster to estimate DSM height above ground from or 2,3) path to a directory containing DTM tiles whose file names match the DSM tiles. Each DSM must be a  single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dtm { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating mean ground elevations. Default is null, which accepts any single band raster.")]
        public string? DtmBand { get; set; }

        [Parameter(HelpMessage = "Settings controlling which grid metrics the output rasters contain.")]
        [ValidateNotNull]
        public GridMetricsSettings Settings { get; set; }

        // quick solution for constraining memory consumption
        // A more adaptive implementation would track memory consumption (e.g. GlobalMemoryStatusEx()), estimate it from the size of the
        // tiles, or use a reasonably stable bound such as the total number of points loaded.
        [Parameter(HelpMessage = "Maximum number of point cloud tiles to have fully loaded at the same time (default is 25 or the number of physical cores, whichever is higher). This is a safeguard to constrain maximum memory consumption in situations where tiles are loaded faster than DSMs can be created.")]
        [ValidateRange(1, 256)] // arbitrary upper bound
        public int MaxPointTiles { get; set; }

        public GetGridMetrics()
        {
            this.CellBand = null;
            this.Dtm = String.Empty;
            this.DtmBand = null;
            // leave this.MaxThreads at default for DTM read
            this.MaxPointTiles = -1;
            this.Settings = new();
        }

        protected override void ProcessRecord()
        {
            HardwareCapabilities hardwareCapabilities = HardwareCapabilities.Current;
            if (this.MaxPointTiles == -1)
            {
                this.MaxPointTiles = Int32.Max(25, hardwareCapabilities.PhysicalCores);
            }
            if (this.MaxThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxThreads), "-" + nameof(this.MaxThreads) + " must be at least two.");
            }

            using Dataset gridCellDefinitionDataset = Gdal.Open(this.Cells, Access.GA_ReadOnly);
            Raster cellDefinitions = Raster.Read(this.Cells, gridCellDefinitionDataset, readData: true);
            int gridEpsg = cellDefinitions.Crs.ParseEpsg();

            const string cmdletName = "Get-GridMetrics";
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, gridEpsg);

            VirtualRaster<Raster<float>> dtm = this.ReadVirtualRaster<Raster<float>>(cmdletName, this.Dtm, readData: true);
            if (SpatialReferenceExtensions.IsSameCrs(lasGrid.Crs, dtm.Crs) == false)
            {
                throw new NotSupportedException("The point clouds and DTM are currently required to be in the same CRS. The point cloud CRS is '" + lasGrid.Crs.GetName() + "' while the DTM CRS is " + dtm.Crs.GetName() + ".");
            }

            RasterBand cellMask = cellDefinitions.GetBand(this.CellBand);
            GridMetricsPointLists gridMetrics = new(cellMask, lasGrid); // metrics grid inherits cellDefinitions's CRS, grid CRS can differ from LAS tiles' CRS
            GridMetricsRaster metricsRaster = new(gridMetrics, this.Settings);

            MetricsTileRead metricsTileRead = new(dtm, gridEpsg, gridMetrics.NonNullCells, this.MaxPointTiles);
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetPointsToGridMetricsBandwidth();
            int readThreads = this.GetLasTileReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs, minWorkerThreadsPerReadThread: 1);
            Task[] gridMetricsTasks = new Task[2]; // not currently thread safe so limit ot single thread, default to one calculate thread per read thread
            for (int readThread = 0; readThread < readThreads; ++readThread)
            {
                gridMetricsTasks[readThread] = Task.Run(() => this.ReadLasTiles(lasGrid, gridMetrics, metricsTileRead), metricsTileRead.CancellationTokenSource.Token);
            }
            for (int calculateThread = readThreads; calculateThread < gridMetricsTasks.Length; ++calculateThread)
            {
                gridMetricsTasks[calculateThread] = Task.Run(() => this.WriteTiles(metricsRaster, metricsTileRead), metricsTileRead.CancellationTokenSource.Token);
            }

            TimedProgressRecord gridMetricsProgress = new(cmdletName, "Calculating metrics: " + metricsTileRead.RasterCellsCompleted.ToString("#,#,0") + " of " + metricsTileRead.RasterCells.ToString("#,0") + " cells (" + metricsTileRead.TilesRead + " of " + lasGrid.NonNullCells + " point cloud tiles)...");
            this.WriteProgress(gridMetricsProgress);

            while (Task.WaitAll(gridMetricsTasks, Constant.DefaultProgressInterval) == false)
            {
                // see remarks in LasTilesToTilesCmdlet.WaitForTasks()
                for (int taskIndex = 0; taskIndex < gridMetricsTasks.Length; ++taskIndex)
                {
                    Task task = gridMetricsTasks[taskIndex];
                    if (task.IsFaulted)
                    {
                        metricsTileRead.CancellationTokenSource.Cancel();
                        throw task.Exception;
                    }
                }

                gridMetricsProgress.StatusDescription = "Calculating metrics: " + metricsTileRead.RasterCellsCompleted.ToString("#,#,0") + " of " + metricsTileRead.RasterCells.ToString("#,0") + " cells (" + metricsTileRead.TilesRead + " of " + lasGrid.NonNullCells + " point cloud tiles)...";
                gridMetricsProgress.Update(metricsTileRead.RasterCellsCompleted, metricsTileRead.RasterCells);
                this.WriteProgress(gridMetricsProgress);
            }

            this.WriteObject(metricsRaster);

            gridMetricsProgress.Stopwatch.Stop();
            this.WriteVerbose("Calculated metrics for " + metricsTileRead.RasterCellsCompleted.ToString("n0") + " cells from " + metricsTileRead.TilesRead + " tiles in " + gridMetricsProgress.Stopwatch.ToElapsedString() + ": " + (metricsTileRead.RasterCellsCompleted / gridMetricsProgress.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " cells/s.");
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
                        tileRead.IncrementTilesReadThreadSafe();

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
                        throw new TaskCanceledException("Error calculating metrics for metrics grid cell at position (" + gridCell.XIndex + ", " + gridCell.YIndex + ").", exception, tileRead.CancellationTokenSource.Token);
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

        private class MetricsTileRead : TileRead
        {
            public SpatialReference Crs { get; private init; }
            public VirtualRaster<Raster<float>> DtmTiles { get; private init; }
            public BlockingCollection<List<PointListZirnc>> FullyPopulatedCells { get; private init; }

            public int RasterCells { get; private init; }
            public int RasterCellsCompleted { get; set; }

            public MetricsTileRead(VirtualRaster<Raster<float>> dtmTiles, int gridEpsg, int metricsCells, int maxSimultaneouslyLoadedTiles) 
            {
                this.Crs = new(null);
                this.Crs.ImportFromEpsg(gridEpsg);
                this.DtmTiles = dtmTiles;
                this.FullyPopulatedCells = new(maxSimultaneouslyLoadedTiles);
                this.RasterCells = metricsCells;
                this.RasterCellsCompleted = 0;
            }
        }
    }
}
