using Mars.Clouds.GdalExtensions;
using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Threading.Tasks;
using Mars.Clouds.Las;
using OSGeo.OSR;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;
using System.Reflection.Metadata;
using Mars.Clouds.Extensions;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Dsm")]
    public class GetDsm : LasTileCmdlet
    {
        [Parameter(HelpMessage = "Size of a square DSM cell in the point clouds' CRS units. Must be an integer multiple of the tile size. Default is 0.5 m for metric point clouds and 1.5 feet for point clouds with English units.")]
        public double CellSize { get; set; }

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to write DSM to as a GeoTIFF or 2,3) path to a directory to write DSM tiles to.")]
        [ValidateNotNullOrEmpty]
        public string? Dsm { get; set; }

        [Parameter(HelpMessage = "Isolation distance beyond which topmost points are declared high noise or outliers and excluded from DSM, has no effect if -UpperPoints is 1 and is ignored if -WriteUpperPoints is set. Default is 15 m for metric point clouds and 50 feet for point clouds with English units. Currently, rejection does not consider a cell's neighbors so, if a DSM cell has a single point, that point is always used.")]
        [ValidateRange(0.0F, 500.0F)] // arbitrary upper bound
        public float Isolation { get; set; }

        [Parameter(HelpMessage = "Number of uppermost points to track in each cell to enable outlier rejection. Default is 10 points, meaning up to the nine uppermost points can be declared isolated and excluded from the DSM.")]
        [ValidateRange(1, 100)] // arbitrary upper bound
        public int UpperPoints { get; set; }

        [Parameter(HelpMessage = "If set, write a DSM raster with -UpperPoints containing the sorted points in each cell.")]
        public SwitchParameter WriteUpperPoints { get; set; }

        public GetDsm()
        {
            this.CellSize = -1.0;
            // this.Dsm is mandatory
            this.Isolation = -1.0F;
            this.UpperPoints = 10;
            this.WriteUpperPoints = false;
        }

        protected override void ProcessRecord()
        {
            Debug.Assert(this.Dsm != null);
            bool dsmPathIsDirectory = false;
            if (File.Exists(this.Dsm))
            {
                FileAttributes dsmPathAttributes = File.GetAttributes(this.Dsm);
                dsmPathIsDirectory = dsmPathAttributes.HasFlag(FileAttributes.Directory);
            }

            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(requiredEpsg: null);
            if ((lasGrid.NonNullCells > 1) && (dsmPathIsDirectory == false))
            {
                throw new ParameterOutOfRangeException(nameof(this.Dsm), "-" + nameof(this.Dsm) + " must be an existing directory when " + nameof(this.Las) + " indicates multiple files.");
            }

            double crsLinearUnits = lasGrid.Crs.GetLinearUnits();
            if (this.CellSize < 0.0)
            {
                this.CellSize = crsLinearUnits == 1.0 ? 0.5 : 1.5; // 0.5 m or 1.5 feet
            }
            if (this.Isolation < 0.0F)
            {
                this.Isolation = crsLinearUnits == 1.0 ? 15.0F : 50.0F; // 15 m or 50 feet
            }
            int dsmTileCellsX = (int)(lasGrid.Transform.CellWidth / this.CellSize);
            if (lasGrid.Transform.CellWidth - dsmTileCellsX * this.CellSize != 0.0)
            {
                string units = lasGrid.Crs.GetLinearUnitsName();
                throw new ParameterOutOfRangeException(nameof(this.CellSize), "LAS tile grid pitch of " + lasGrid.Transform.CellWidth + " x " + lasGrid.Transform.CellHeight + " is not an integer multiple of the " + this.CellSize + " " + units + " cell size.");
            }
            int dsmTileCellsY = (int)(Double.Abs(lasGrid.Transform.CellHeight) / this.CellSize);
            if (Double.Abs(lasGrid.Transform.CellHeight) - dsmTileCellsY * this.CellSize != 0.0)
            {
                string units = lasGrid.Crs.GetLinearUnitsName();
                throw new ParameterOutOfRangeException(nameof(this.CellSize), "LAS tile grid pitch of " + lasGrid.Transform.CellWidth + " x " + lasGrid.Transform.CellHeight + " is not an integer multiple of the " + this.CellSize + " " + units + " cell size.");
            }

            Stopwatch stopwatch = new();
            stopwatch.Start();

            CancellationTokenSource cancellationTokenSource = new();
            BlockingCollection<(string, PointGridZ)> loadedTiles = new(this.MaxTiles);
            int lasTilesLoaded = 0;
            Task readPoints = Task.Run(() =>
            {
                try
                {
                    for (int tileYindex = 0; tileYindex < lasGrid.YSize; ++tileYindex)
                    {
                        for (int tileXindex = 0; tileXindex < lasGrid.XSize; ++tileXindex)
                        {
                            LasTile? lasTile = lasGrid[tileXindex, tileYindex];
                            if (lasTile == null)
                            {
                                continue;
                            }

                            GridGeoTransform lasTileTransform = new(lasTile.GridExtent, this.CellSize, this.CellSize);
                            PointGridZ dsmTilePointZ = new(lasTile.GetSpatialReference(), lasTileTransform, dsmTileCellsX, dsmTileCellsY, this.UpperPoints);
                            using LasReader pointReader = lasTile.CreatePointReader();
                            pointReader.ReadUpperPointsToGrid(lasTile, dsmTilePointZ);

                            // check for cancellation before queing tile for metrics calculation
                            // Since tile loads are long, checking immediately before adding mitigates risk of queing blocking because
                            // the metrics task has faulted and the queue is full. (Locking could be used to remove the race condition
                            // entirely, but currently seems unnecessary as this appears to be an edge case.)
                            if (this.Stopping || cancellationTokenSource.IsCancellationRequested)
                            {
                                break;
                            }

                            string tileName = Tile.GetName(lasTile.FilePath);
                            loadedTiles.Add((tileName, dsmTilePointZ));
                            ++lasTilesLoaded;

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
                    loadedTiles.CompleteAdding();
                }
            }, cancellationTokenSource.Token);

            int dsmCellsWritten = 0;
            int dsmTilesWritten = 0;
            Task calculateMetrics = Task.Run(() =>
            {
                SpatialReference crs = new(null);
                crs.ImportFromEPSG(lasGrid.Crs.ParseEpsg());
                float crsLinearUnits = (float)crs.GetLinearUnits();
                float oneMeterHeightClass = 1.0F / crsLinearUnits;
                float twoMeterHeightThreshold = 2.0F / crsLinearUnits; // applied relative to mean ground height in each cell if ground points are classified, used as is if points aren't classified

                foreach ((string tileName, PointGridZ dsmTilePointZ) in loadedTiles.GetConsumingEnumerable())
                {
                    try
                    {
                        Raster<float> dsmTile;
                        if (this.WriteUpperPoints)
                        {
                            dsmTile = dsmTilePointZ.GetUpperPoints();
                        }
                        else
                        {
                            dsmTile = dsmTilePointZ.GetDigitalSurfaceModel(this.Isolation);
                        }

                        string dsmTilePath = dsmPathIsDirectory ? Path.Combine(this.Dsm, tileName + Constant.File.GeoTiffExtension) : this.Dsm;
                        dsmTile.Write(dsmTilePath);

                        dsmCellsWritten += dsmTile.XSize * dsmTile.YSize;
                        ++dsmTilesWritten;
                    }
                    catch (Exception exception)
                    {
                        throw new TaskCanceledException("Error calculating DSM for tile with extent (" + dsmTilePointZ.GetExtentString() + ").", exception, cancellationTokenSource.Token);
                    }

                    if (this.Stopping || cancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }, cancellationTokenSource.Token);

            ProgressRecord dsmProgress = new(0, "Get-Dsm", "tile " + dsmTilesWritten.ToString("#,#,0") + " of " + lasTilesLoaded + "...");
            this.WriteProgress(dsmProgress);

            TimeSpan progressUpdateInterval = TimeSpan.FromSeconds(10.0);
            Task[] gridMetricsTasks = [readPoints, calculateMetrics];
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

                float fractionComplete = (float)dsmTilesWritten / (float)lasTilesLoaded;
                dsmProgress.StatusDescription = "tile " + dsmTilesWritten.ToString("#,#,0") + " of " + lasTilesLoaded + "...";
                dsmProgress.PercentComplete = (int)(100.0F * fractionComplete);
                dsmProgress.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                this.WriteProgress(dsmProgress);
            }

            stopwatch.Stop();

            string elapsedTimeFormat = stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Found DSM elevations for " + dsmCellsWritten.ToString("#,#,0") + " cells from " + lasTilesLoaded + " LAS tiles in " + stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + (dsmTilesWritten / stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " tiles/s.");
            base.ProcessRecord();
        }
    }
}
