using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System;
using System.Management.Automation;
using System.Threading.Tasks;

namespace Mars.Clouds.Cmdlets
{
    public class LasTilesToTilesCmdlet : LasTilesCmdlet
    {
        [Parameter(HelpMessage = "Size of a DSM cell or orthoimage pixel in the point clouds' CRS units. Must be an integer multiple of the tile size. Default is 0.5 m for metric point clouds and 1.5 feet for point clouds with English units.")]
        public double CellSize { get; set; }

        [Parameter(HelpMessage = "Whether or not to compress output rasters (DSM or orthoimages). Default is false.")]
        public SwitchParameter CompressRasters { get; set; }

        protected LasTilesToTilesCmdlet()
        {
            this.CellSize = -1.0;
            this.CompressRasters = false;
        }

        private static string GetLasReadTileWriteStatusDescription(LasTileGrid lasGrid, TileReadWrite tileReadWrite)
        {
            string status = tileReadWrite.TilesLoaded + (tileReadWrite.TilesLoaded == 1 ? " point cloud tile read, " : " point cloud tiles read, ") +
                            tileReadWrite.TilesWritten + " of " + lasGrid.NonNullCells + " tiles written...";
            return status;
        }

        protected (LasTileGrid lasGrid, int tileSizeX, int tileSizeY) ReadLasHeadersAndCellSize(string cmdletName, string outputParameterName, bool outputPathIsDirectory)
        {
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, requiredEpsg: null);
            if ((lasGrid.NonNullCells > 1) && (outputPathIsDirectory == false))
            {
                throw new ParameterOutOfRangeException(outputParameterName, "-" + outputParameterName + " must be an existing directory when " + nameof(this.Las) + " indicates multiple files.");
            }

            if (this.CellSize < 0.0)
            {
                double crsLinearUnits = lasGrid.Crs.GetLinearUnits();
                this.CellSize = crsLinearUnits == 1.0 ? 0.5 : 1.5; // 0.5 m or 1.5 feet
            }

            int outputTileSizeX = (int)(lasGrid.Transform.CellWidth / this.CellSize);
            if (lasGrid.Transform.CellWidth - outputTileSizeX * this.CellSize != 0.0)
            {
                string units = lasGrid.Crs.GetLinearUnitsName();
                throw new ParameterOutOfRangeException(nameof(this.CellSize), "Point cloud tile grid pitch of " + lasGrid.Transform.CellWidth + " x " + lasGrid.Transform.CellHeight + " is not an integer multiple of the " + this.CellSize + " " + units + " output cell size.");
            }
            int outputTileSizeY = (int)(Double.Abs(lasGrid.Transform.CellHeight) / this.CellSize);
            if (Double.Abs(lasGrid.Transform.CellHeight) - outputTileSizeY * this.CellSize != 0.0)
            {
                string units = lasGrid.Crs.GetLinearUnitsName();
                throw new ParameterOutOfRangeException(nameof(this.CellSize), "Point cloud tile grid pitch of " + lasGrid.Transform.CellWidth + " x " + lasGrid.Transform.CellHeight + " is not an integer multiple of the " + this.CellSize + " " + units + " output cell size.");
            }

            return (lasGrid, outputTileSizeX, outputTileSizeY);
        }

        protected void ReadLasTiles<TTile, TReadWrite>(LasTileGrid lasGrid, Func<LasTile, TReadWrite, TTile> readTile, TReadWrite tileReadWrite) where TReadWrite : TileReadWrite<TTile>
        {
            try
            {
                // load tiles in grid index order for rowwise neighborhood completion
                // In cases where processing of individual tiles needs to consider data in adacent tiles, some type of structured completion
                // is needed to be able to stream large datasets through memory. Currently, tiles are read in grid order which, being row
                // major, means processing of tiles in row n can check the current tile read index to see if tiles in rows n - 1 or n + 1 are
                // available.
                // This design relies on the loaded tiles collection being first in, first out.
                for (int tileIndex = tileReadWrite.GetNextTileReadIndexThreadSafe(); tileIndex < lasGrid.Cells; tileIndex = tileReadWrite.GetNextTileReadIndexThreadSafe())
                {
                    LasTile? lasTile = lasGrid[tileIndex];
                    if (lasTile == null)
                    {
                        continue; // nothing to do as no tile is present at this grid position
                    }

                    TTile parsedLasTile = readTile(lasTile, tileReadWrite);

                    // check for cancellation before queing tile for metrics calculation
                    // Since tile loads are long, checking immediately before adding mitigates risk of queing blocking because
                    // the metrics task has faulted and the queue is full. (Locking could be used to remove the race condition
                    // entirely, but currently seems unnecessary as this appears to be an edge case.)
                    if (this.Stopping || tileReadWrite.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }

                    string tileName = Tile.GetName(lasTile.FilePath);
                    lock (tileReadWrite)
                    {
                        tileReadWrite.LoadedTiles.Add((tileName, parsedLasTile));
                        ++tileReadWrite.TilesLoaded;

                        if ((tileReadWrite.TilesLoaded == lasGrid.NonNullCells) && (tileReadWrite.LoadedTiles.IsAddingCompleted == false))
                        {
                            tileReadWrite.LoadedTiles.CompleteAdding();
                        }
                    }

                    //FileInfo fileInfo = new(this.Las);
                    //UInt64 pointsRead = lasFile.Header.GetNumberOfPoints();
                    //float megabytesRead = fileInfo.Length / (1024.0F * 1024.0F);
                    //float gigabytesRead = megabytesRead / 1024.0F;
                    //double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                    //this.WriteVerbose("Gridded " + gigabytesRead.ToString("0.00") + " GB with " + pointsRead.ToString("#,0") + " points into " + abaGridCellsWithPoints + " grid cells in " + elapsedSeconds.ToString("0.000") + " s: " + (pointsRead / (1E6 * elapsedSeconds)).ToString("0.00") + " Mpoints/s, " + (megabytesRead / elapsedSeconds).ToString("0.0") + " MB/s.");
                }
            }
            catch (Exception)
            {
                // ensure tile processing doesn't block indefinitely waiting for more data if an exception occurs during tile loading
                if (tileReadWrite.LoadedTiles.IsAddingCompleted == false)
                {
                    tileReadWrite.LoadedTiles.CompleteAdding();
                }
                throw;
            }
        }

        protected void WaitForLasReadTileWriteTasks(string cmdletName, Task[] tasks, LasTileGrid lasGrid, TileReadWrite tileReadWrite)
        {
            ProgressRecord readWriteProgress = new(0, cmdletName, LasTilesToTilesCmdlet.GetLasReadTileWriteStatusDescription(lasGrid, tileReadWrite));
            this.WriteProgress(readWriteProgress);

            while (Task.WaitAll(tasks, LasTilesCmdlet.ProgressUpdateInterval) == false)
            {
                // unlike Task.WaitAll(Task[]), Task.WaitAll(Task[], TimeSpan) does not unblock and throw the exception if any task faults
                // If one task has faulted then cancellation is therefore desirable to stop the other tasks. If tile read faults metrics
                // calculation can see no more tiles will be added and can complete normally, leading to incomplete metrics in any grid cells
                // which weren't compeletely read. If metrics calculation faults the cmdlet will exit but, if not cancelled, tile read continues
                // until blocking indefinitely when maximum tile load is reached.
                for (int taskIndex = 0; taskIndex < tasks.Length; ++taskIndex)
                {
                    Task task = tasks[taskIndex];
                    if (task.IsFaulted)
                    {
                        tileReadWrite.CancellationTokenSource.Cancel();
                        throw task.Exception;
                    }
                }

                float fractionComplete = 0.5F * (float)(tileReadWrite.TilesLoaded + tileReadWrite.TilesWritten) / (float)lasGrid.NonNullCells;
                readWriteProgress.StatusDescription = LasTilesToTilesCmdlet.GetLasReadTileWriteStatusDescription(lasGrid, tileReadWrite);
                readWriteProgress.PercentComplete = (int)(100.0F * fractionComplete);
                readWriteProgress.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(tileReadWrite.Stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                this.WriteProgress(readWriteProgress);
            }

            tileReadWrite.Stopwatch.Stop();
        }

        protected void WriteTiles<TTile, TReadWrite>(Func<string, TTile, TReadWrite, int> writeTile, TReadWrite tileReadWrite) where TTile : Grid where TReadWrite : TileReadWrite<TTile>
        {
            foreach ((string tileName, TTile dsmTilePointZ) in tileReadWrite.LoadedTiles.GetConsumingEnumerable())
            {
                try
                {
                    int cellsInTile = writeTile(tileName, dsmTilePointZ, tileReadWrite);

                    lock (tileReadWrite)
                    {
                        tileReadWrite.CellsWritten += cellsInTile;
                        ++tileReadWrite.TilesWritten;
                    }
                }
                catch (Exception exception)
                {
                    throw new TaskCanceledException("Failed to process tile '" + tileName + "' with extent (" + dsmTilePointZ.GetExtentString() + ").", exception, tileReadWrite.CancellationTokenSource.Token);
                }

                if (this.Stopping || tileReadWrite.CancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
