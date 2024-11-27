using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using Mars.Clouds.Vrt;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    /// <summary>
    /// Caclculate gridded point cloud metrics (for area based approaches) using a set of point cloud tiles and a raster defining the grid cells.
    /// </summary>
    /// <remarks>
    /// Operates in two modes.
    /// 1) If presented with a grid cell template using -Cells, generates a monolithic grid metrics raster. This use case aligns primarily with 
    ///    the 20–40 m grid sizes commonly used with LiDAR or photogrammetric area based approaches to forest inventory. The code is cell size 
    ///    agnostic, however, and can also be used to get point cloud informatics at submeter resolutions or at larger scales.
    /// 2) If given a cell size with -CellSize, writes a grid metrics tile for each input point cloud tile to the directory indicated by -Metrics.
    ///    Tile sizes must be exactly divisible by the cell size.
    /// </remarks>
    [Cmdlet(VerbsCommon.Get, "GridMetrics")]
    public class GetGridMetrics : LasTilesToRasterCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "1) path to a single digital terrain model (DTM) raster to estimate DSM height above ground from or 2,3) path to a directory containing DTM tiles whose file names match the DSM tiles. Each DSM must be a  single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dtm { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Size, in point cloud CRS units, of cells in output grid metrics tiles matching the input point cloud tiles. Mutually exclusive with -Cells and, if used, the point cloud tile spacing must be an integer multiple of the cell size.")]
        [ValidateRange(0.0F, 1000.0F)] // arbitrary upper bound
        public double CellSize { get; set; }

        [Parameter(HelpMessage = "Output path to write grid metrics to. If -Cells is specified, must contain a file name for the monolithic raster generated. If -CellSize is used, must indicate a directory to write grid metrics tiles to.")]
        [ValidateNotNullOrWhiteSpace]
        public string Metrics { get; set; }

        [Parameter(HelpMessage = "Settings controlling which grid metrics the output rasters contain.")]
        [ValidateNotNull]
        public GridMetricsSettings Settings { get; set; }

        [Parameter(HelpMessage = "Name of band in -Cells to use in defining grid cells. Default is the first band.")]
        public string? CellBand { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating mean ground elevations. Default is null, which accepts any single band raster.")]
        public string? DtmBand { get; set; }

        [Parameter(HelpMessage = "Whether or not to create a virtual raster of the metrics tiles generated.")]
        public SwitchParameter Vrt { get; set; }

        [Parameter(HelpMessage = "Whether or not to compress slope and aspect rasters. Default is false.")]
        public SwitchParameter CompressRasters { get; set; }

        [Parameter(HelpMessage = "Perform all processing steps except writing output tiles to disk. This development and diangostic switch provides insight in certain types of performance profiling and it allows evaluation of drive thermals without incurring flash write wear.")]
        public SwitchParameter NoWrite { get; set; }

        public GetGridMetrics()
        {
            this.CellBand = null;
            this.CellSize = Double.NaN;
            this.CompressRasters = false;
            this.DataThreads = Environment.ProcessorCount; // ~20% gain over defaulting to physical core count on Zen 5
            this.Dtm = String.Empty;
            this.DtmBand = null;
            // leave this.MetadataThreads at default for LAS header and DTM read
            this.Metrics = String.Empty;
            this.Settings = new();
        }

        protected override void ProcessRecord()
        {
            if (this.DataThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.DataThreads), "-" + nameof(this.DataThreads) + " must be at least two.");
            }
            if ((String.IsNullOrWhiteSpace(this.Cells) == false) && this.Vrt)
            {
                throw new NotSupportedException("A virtual raster cannot be generated when the output is a single metrics raster. Specify either -" + nameof(this.Cells) + " or -" + nameof(this.Vrt) + " but not both.");
            }

            const string cmdletName = "Get-GridMetrics";
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName);
            if ((lasGrid.Transform.ColumnRotation != 0.0) || (lasGrid.Transform.RowRotation != 0.0))
            {
                throw new NotSupportedException("-" + nameof(this.Las) + " indicates a point cloud or point cloud tiles ('" + this.Las + "') which are rotated with respect to their coordinate system. Currently only tiles aligned with their coordinate system's axes are supported.");
            }

            RasterBand? metricsCellMask = null;
            GridMetricsRaster? metricsRasterMonolithic = null;
            double tileSizeInFractionalCellsX = lasGrid.Transform.CellWidth / this.CellSize;
            double tileSizeInFractionalCellsY = Double.Abs(lasGrid.Transform.CellHeight) / this.CellSize;
            int tileSizeInCellsX = (int)tileSizeInFractionalCellsX;
            int tileSizeInCellsY = (int)tileSizeInFractionalCellsY;
            if (String.IsNullOrWhiteSpace(this.Cells))
            {
                if (Double.IsNaN(this.CellSize) || (this.CellSize <= 0.0))
                {
                    throw new ParameterOutOfRangeException(nameof(this.CellSize), "If -" + nameof(this.Cells) + " is not specified then -" + nameof(this.CellSize) + " must be set to a positive value.");
                }

                if ((Double.Abs(tileSizeInFractionalCellsX - tileSizeInCellsX) > 1E-9) || (Double.Abs(tileSizeInFractionalCellsY - tileSizeInCellsY) > 1E-9)) // integer truncation and tolerances mean Abs() shouldn't be needed but Abs() just in case of unanticipated numerical edge conditions
                {
                    throw new ParameterOutOfRangeException(nameof(this.CellSize), "A -" + nameof(this.CellSize) + " of " + this.CellSize + " results in grid metrics tiles of " + tileSizeInFractionalCellsX + " by " + tileSizeInFractionalCellsY + " cells. The tile size (" + lasGrid.Transform.CellWidth + " by " + lasGrid.Transform.CellHeight + ") must be an integer multiple of the cell size. If the point clouds' bounding boxes do not entirely fill their tile size specifying -" + nameof(this.Snap) + " may help.");
                }
            }
            else
            {
                using Dataset gridCellDefinitionDataset = Gdal.Open(this.Cells, Access.GA_ReadOnly);
                Raster cellDefinitions = Raster.Create(this.Cells, gridCellDefinitionDataset, readData: false);
                if (SpatialReferenceExtensions.IsSameCrs(lasGrid.Crs, cellDefinitions.Crs) == false)
                {
                    throw new NotSupportedException("The point clouds and cell definitions are currently required to be in the same CRS. The point cloud CRS is '" + lasGrid.Crs.GetName() + "' while cells are defined in " + cellDefinitions.Crs.GetName() + ".");
                }
                if ((cellDefinitions.Transform.ColumnRotation != 0.0) || (cellDefinitions.Transform.RowRotation != 0.0))
                {
                    throw new NotSupportedException("'" + this.Cells + "' is rotated with respect to its coordinate system. Rotated metrics cell definition rasters are not currently supported.");
                }

                metricsCellMask = cellDefinitions.GetBand(this.CellBand);
                metricsCellMask.ReadDataInSameCrsAndTransform(gridCellDefinitionDataset);
                gridCellDefinitionDataset.FlushCache();

                metricsRasterMonolithic = new(metricsCellMask, this.Settings);

                // increase point tile size by one row and column to support tile sizes which aren't exact multiples of the metrics cell size
                // This passing is sufficient only when the tile and metrics grids' axes are aligned, which is the net effect of same horizontal
                // CRS checks, no rotation checks on rasters, and .las files' lack of rotated bounding box support.
                ++tileSizeInCellsX;
                ++tileSizeInCellsY;
            }

            VirtualRaster<Raster<float>> dtm = this.ReadVirtualRasterMetadata<Raster<float>>(cmdletName, this.Dtm, Raster<float>.CreateFromBandMetadata, this.CancellationTokenSource);
            if (SpatialReferenceExtensions.IsSameCrs(lasGrid.Crs, dtm.Crs) == false)
            {
                throw new NotSupportedException("The point clouds and DTM are currently required to be in the same CRS. The point cloud CRS is '" + lasGrid.Crs.GetName() + "' while the DTM CRS is " + dtm.Crs.GetName() + ".");
            }

            HardwareCapabilities hardwareCapabilities = HardwareCapabilities.Current;
            float crsLinearUnits = (float)lasGrid.Crs.GetLinearUnits();
            float oneMeterHeightClass = 1.0F / crsLinearUnits;
            float twoMeterHeightThreshold = 2.0F / crsLinearUnits; // applied relative to mean ground height in each cell if ground points are classified, used as is if points aren't classified

            bool metricsPathIsDirectory = Directory.Exists(this.Metrics);
            VirtualRaster<GridMetricsRaster> metrics = new(lasGrid);
            GridMetricsTileReadCreateWrite metricsReadCreateWrite = GridMetricsTileReadCreateWrite.Create(lasGrid, dtm, metrics, metricsRasterMonolithic, metricsPathIsDirectory, this.NoWrite, this.CompressRasters);
            Debug.Assert(metrics.TileGrid != null);
            GridNullable<List<RasterBandStatistics>>? metricsStatistics = this.Vrt ? new(metrics.TileGrid, cloneCrsAndTransform: false) : null;

            // TODO: constain number of data threads based on physical DDR and estimated memory consumption per thread
            // 24 threads @ ~3 GB .las tiles (point format 8; 37 bytes -> 4+2+1+1 = 8 bytes zirnc) + 200x200x40 metrics = ~60 GB => 2.5 GB/thread
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetPointsToGridMetricsBandwidth();
            int readThreads = this.GetLasTileReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs, minWorkerThreadsPerReadThread: 1);
            int maxUsefulThreads = Int32.Min(lasGrid.NonNullCells, 3 * readThreads);
            ObjectPool<PointListZirnc> pointListPool = new();
            using SemaphoreSlim readSemaphore = new(initialCount: readThreads, maxCount: readThreads);
            ParallelTasks gridMetricsTasks = new(Int32.Min(maxUsefulThreads, this.DataThreads), () =>
            {
                GridMetricsPointLists? tilePoints = null;
                while (metricsReadCreateWrite.TileWritesInitiated < lasGrid.NonNullCells)
                {
                    metricsReadCreateWrite.TryWriteCompletedTiles(this.CancellationTokenSource, metricsStatistics);

                    readSemaphore.Wait(this.CancellationTokenSource.Token);
                    if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                    {
                        readSemaphore.Release();
                        return;
                    }

                    LasTile? lasTile = null;
                    for (int tileIndex = metricsReadCreateWrite.GetNextTileReadIndexThreadSafe(); tileIndex < metricsReadCreateWrite.MaxTileIndex; tileIndex = metricsReadCreateWrite.GetNextTileReadIndexThreadSafe())
                    {
                        lasTile = lasGrid[tileIndex];
                        if (lasTile == null)
                        {
                            continue; // nothing to do as no tile is present at this grid position
                        }

                        // read tile's DTM
                        // In general, the .las tile grid and DTM tile grid need to match. In the single point cloud case, however, the cloud
                        // is free to fit within the tile it's on.
                        Raster<float> dtmTile = dtm.GetMatchingOrEncompassingTile(lasTile);
                        RasterBand<float> dtmTileBand = dtmTile.GetBand(this.DtmBand);
                        lock (metricsReadCreateWrite)
                        {
                            dtmTileBand.TryTakeOwnershipOfDataBuffer(metricsReadCreateWrite.DtmBandPool);
                        }
                        dtmTileBand.Read(dtmTile.FilePath);

                        // read .las points
                        using LasReader pointReader = lasTile.CreatePointReader();
                        if (tilePoints == null)
                        {
                            tilePoints = new(lasGrid, lasTile, this.CellSize, tileSizeInCellsX, tileSizeInCellsY, metricsCellMask);
                        }
                        else
                        {
                            tilePoints.Reset(lasGrid, lasTile, metricsCellMask);
                        }

                        // read tile points
                        pointReader.ReadPointsToGrid(lasTile, tilePoints);
                        readSemaphore.Release(); // exit semaphore as .las file has been read
                        (int tileIndexX, int tileIndexY) = lasGrid.ToGridIndices(tileIndex);
                        lock (metricsReadCreateWrite)
                        {
                            metricsReadCreateWrite.OnTileRead(tileIndexX, tileIndexY);
                        }

                        // calculate grid metrics over tile
                        GridMetricsRaster metricsRaster;
                        if (metricsRasterMonolithic == null)
                        {
                            string tileName = Tile.GetName(lasTile.FilePath);
                            string metricsTilePath = metricsReadCreateWrite.OutputPathIsDirectory ? LasTilesCmdlet.GetRasterTilePath(this.Metrics, tileName) : this.Metrics;
                            lock (metricsReadCreateWrite)
                            {
                                metricsRaster = new(lasGrid.Crs, lasTile, this.CellSize, tileSizeInCellsX, tileSizeInCellsY, this.Settings, metricsReadCreateWrite.WriteBandPool)
                                {
                                    FilePath = metricsTilePath,
                                };
                            }
                        }
                        else
                        {
                            metricsRaster = metricsRasterMonolithic;
                        }

                        (double tileCentroidX, double tileCentroidY) = tilePoints.GetCentroid();
                        if (dtm.TryGetNeighborhood8(tileCentroidX, tileCentroidY, this.DtmBand, out RasterNeighborhood8<float>? dtmNeighborhood) == false)
                        {
                            throw new InvalidOperationException("Could not find DTM tile for metrics grid cell at (" + tileCentroidX + ", " + tileCentroidY + ").");
                        }

                        int metricsCellsCompleted = tilePoints.EvaluateCompleteAndAccumulateIncompleteCells(metricsRaster, dtmNeighborhood, oneMeterHeightClass, twoMeterHeightThreshold, pointListPool);
                        lock (metricsReadCreateWrite)
                        {
                            metricsReadCreateWrite.MetricsCellsCompleted += metricsCellsCompleted;
                            metricsReadCreateWrite.PointsCompleted += lasTile.Header.GetNumberOfPoints();
                            if (metricsRasterMonolithic == null)
                            {
                                metrics.Add(tileIndexX, tileIndexY, metricsRaster);
                                metricsReadCreateWrite.OnTileCreated(tileIndexX, tileIndexY);
                            }
                        }

                        // a tile's points have been read so exit load loop to check for point lists which can be flushed to the metrics raster
                        break;
                    }

                    if (lasTile == null)
                    {
                        // end of input tiles has been reached but semaphore was taken and still needs to be released
                        // If semaphore is not released any other worker threads blocked on it can't enter it and thus can't return.
                        readSemaphore.Release();
                    }

                    if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }, this.CancellationTokenSource);

            int activeReadThreads = readThreads - readSemaphore.CurrentCount;
            TimedProgressRecord gridMetricsProgress = new(cmdletName, metricsReadCreateWrite.TilesRead + " of " + lasGrid.NonNullCells + " tiles, " + metricsReadCreateWrite.MetricsCellsCompleted.ToString("#,#,0") + " metrics cells (" + gridMetricsTasks.Count + " threads, " + activeReadThreads + " reading)...");
            this.WriteProgress(gridMetricsProgress);
            while (gridMetricsTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                activeReadThreads = readThreads - readSemaphore.CurrentCount;
                gridMetricsProgress.StatusDescription = metricsReadCreateWrite.TilesRead + " of " + lasGrid.NonNullCells + " tiles, " + metricsReadCreateWrite.MetricsCellsCompleted.ToString("#,#,0") + " metrics cells (" + gridMetricsTasks.Count + " threads, " + activeReadThreads + " reading)...";
                gridMetricsProgress.Update(metricsReadCreateWrite.TilesCreated, lasGrid.NonNullCells);
                this.WriteProgress(gridMetricsProgress);
            }

            if ((metricsReadCreateWrite.BypassOutputRasterWriteToDisk == false) && (metricsRasterMonolithic != null))
            {
                if (String.IsNullOrWhiteSpace(this.Metrics))
                {
                    this.WriteObject(metricsRasterMonolithic);
                }
                else
                {
                    gridMetricsProgress.StatusDescription = "Writing metrics raster...";
                    gridMetricsProgress.PercentComplete = 0;
                    gridMetricsProgress.SecondsRemaining = -1;
                    this.WriteProgress(gridMetricsProgress);

                    metricsRasterMonolithic.Write(this.Metrics, metricsReadCreateWrite.CompressRasters);
                }
            }
            if (this.Vrt) // debatable if this.NoWrite should be considered here; for now treat -Vrt as an independent switch
            {
                (string metricsVrtFilePath, string metricsVrtDatasetPath) = VrtDataset.GetVrtPaths(this.Metrics, metricsReadCreateWrite.OutputPathIsDirectory, subdirectory: null, "gridMetrics.vrt");
                VrtDataset metricsVrt = metrics.CreateDataset(metricsVrtDatasetPath, metricsStatistics);
                metricsVrt.WriteXml(metricsVrtFilePath);
            }

            gridMetricsProgress.Stopwatch.Stop();
            double cellsPerSecond = metricsReadCreateWrite.MetricsCellsCompleted / gridMetricsProgress.Stopwatch.Elapsed.TotalSeconds;
            string cellsPerSecondFormat = cellsPerSecond >= 9999.5 ? "n0" : "0";
            this.WriteVerbose("Calculated metrics for " + metricsReadCreateWrite.MetricsCellsCompleted.ToString("n0") + " grid cells from " + metricsReadCreateWrite.PointsCompleted.ToString("n0") + " points in " + metricsReadCreateWrite.TilesRead + (metricsReadCreateWrite.TilesRead == 1 ? " tile in " : " tiles in ") + gridMetricsProgress.Stopwatch.ToElapsedString() + ": " + cellsPerSecond.ToString(cellsPerSecondFormat) + " cells/s.");
            base.ProcessRecord();
        }

        private class GridMetricsTileReadCreateWrite : TileReadCreateWriteStreaming<LasTileGrid, LasTile, GridMetricsRaster>
        {
            public VirtualRaster<Raster<float>> Dtm { get; set; }
            public RasterBandPool DtmBandPool { get; private init; }
            public int MetricsCellsCompleted { get; set; }
            public GridNullable<GridMetricsRaster> MetricsGrid { get; private init; }
            public UInt64 PointsCompleted { get; set; }

            protected GridMetricsTileReadCreateWrite(LasTileGrid lasGrid, VirtualRaster<Raster<float>> dtm, bool[,] unpopulatedTileMapForRead, GridNullable<GridMetricsRaster> metricsGrid, bool[,] unpopulatedTileMapForCreate, bool[,] unpopulatedTileMapForWrite, bool metricsPathIsDirectory) 
                : base(lasGrid, unpopulatedTileMapForRead, metricsGrid, unpopulatedTileMapForCreate, unpopulatedTileMapForWrite, metricsPathIsDirectory)
            {
                this.Dtm = dtm;
                this.DtmBandPool = new();
                this.MetricsCellsCompleted = 0;
                this.MetricsGrid = metricsGrid;
                this.PointsCompleted = 0;
            }

            public static GridMetricsTileReadCreateWrite Create(LasTileGrid lasGrid, VirtualRaster<Raster<float>> dtm, VirtualRaster<GridMetricsRaster> metrics, GridMetricsRaster? metricsRasterMonolithic, bool metricsPathIsDirectory, bool bypassOutputRasterWriteToDisk, bool compressRasters)
            {
                GridNullable<GridMetricsRaster> metricsGrid;
                if (metricsRasterMonolithic == null)
                {
                    Debug.Assert(metrics.TileGrid != null);
                    metricsGrid = metrics.TileGrid;
                }
                else
                {
                    GridGeoTransform metricsRasterAsSingleTile = new(metricsRasterMonolithic.Transform.OriginX, metricsRasterMonolithic.Transform.OriginY, metricsRasterMonolithic.Transform.CellWidth * metricsRasterMonolithic.SizeX, metricsRasterMonolithic.Transform.CellHeight * metricsRasterMonolithic.SizeY);
                    metricsGrid = new(lasGrid.Crs.Clone(), metricsRasterAsSingleTile, 1, 1, cloneCrsAndTransform: false);
                }

                bool[,] unpopulatedTileMapForRead = lasGrid.GetUnpopulatedCellMap();
                bool[,] unpopulatedTileMapForCreate = ArrayExtensions.Copy(unpopulatedTileMapForRead);
                bool[,] unpopulatedTileMapForWrite = ArrayExtensions.Copy(unpopulatedTileMapForRead);
                return new(lasGrid, dtm, unpopulatedTileMapForRead, metricsGrid, unpopulatedTileMapForCreate, unpopulatedTileMapForWrite, metricsPathIsDirectory)
                {
                    BypassOutputRasterWriteToDisk = bypassOutputRasterWriteToDisk,
                    CompressRasters = compressRasters
                };
            }

            protected override void OnCreatedTileUnreferenced(int unreferencedTileIndexX, int unreferencedTileIndexY, GridMetricsRaster metricsTile)
            {
                // return grid metrics bands
                base.OnCreatedTileUnreferenced(unreferencedTileIndexX, unreferencedTileIndexY, metricsTile);

                // return DTM band
                Raster<float>? dtmTile = this.Dtm[unreferencedTileIndexX, unreferencedTileIndexY];
                dtmTile?.ReturnBandData(this.DtmBandPool);
            }
        }
    }
}
