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

        protected LasTilesToTilesCmdlet()
        {
            this.CellSize = -1.0;
        }

        protected (LasTileGrid lasGrid, int tileSizeX, int tileSizeY) ReadLasHeadersAndCellSize(string outputParameterName, bool outputPathIsDirectory)
        {
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(requiredEpsg: null);
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
                throw new ParameterOutOfRangeException(nameof(this.CellSize), "LAS tile grid pitch of " + lasGrid.Transform.CellWidth + " x " + lasGrid.Transform.CellHeight + " is not an integer multiple of the " + this.CellSize + " " + units + " output cell size.");
            }
            int outputTileSizeY = (int)(Double.Abs(lasGrid.Transform.CellHeight) / this.CellSize);
            if (Double.Abs(lasGrid.Transform.CellHeight) - outputTileSizeY * this.CellSize != 0.0)
            {
                string units = lasGrid.Crs.GetLinearUnitsName();
                throw new ParameterOutOfRangeException(nameof(this.CellSize), "LAS tile grid pitch of " + lasGrid.Transform.CellWidth + " x " + lasGrid.Transform.CellHeight + " is not an integer multiple of the " + this.CellSize + " " + units + " output cell size.");
            }

            return (lasGrid, outputTileSizeX, outputTileSizeY);
        }

        protected void ReadTiles<TTile>(LasTileGrid lasGrid, Func<LasTile, TileReadWrite<TTile>, TTile> readTile, TileReadWrite<TTile> tileReadWrite)
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

                        TTile imageTile = readTile(lasTile, tileReadWrite);

                        // check for cancellation before queing tile for metrics calculation
                        // Since tile loads are long, checking immediately before adding mitigates risk of queing blocking because
                        // the metrics task has faulted and the queue is full. (Locking could be used to remove the race condition
                        // entirely, but currently seems unnecessary as this appears to be an edge case.)
                        if (this.Stopping || tileReadWrite.CancellationTokenSource.IsCancellationRequested)
                        {
                            break;
                        }

                        string tileName = Tile.GetName(lasTile.FilePath);
                        tileReadWrite.LoadedTiles.Add((tileName, imageTile));
                        ++tileReadWrite.TilesLoaded;

                        //FileInfo fileInfo = new(this.Las);
                        //UInt64 pointsRead = lasFile.Header.GetNumberOfPoints();
                        //float megabytesRead = fileInfo.Length / (1024.0F * 1024.0F);
                        //float gigabytesRead = megabytesRead / 1024.0F;
                        //double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                        //this.WriteVerbose("Gridded " + gigabytesRead.ToString("0.00") + " GB with " + pointsRead.ToString("#,0") + " points into " + abaGridCellsWithPoints + " grid cells in " + elapsedSeconds.ToString("0.000") + " s: " + (pointsRead / (1E6 * elapsedSeconds)).ToString("0.00") + " Mpoints/s, " + (megabytesRead / elapsedSeconds).ToString("0.0") + " MB/s.");
                    }

                    if (this.Stopping || tileReadWrite.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            finally
            {
                // ensure metrics calculation doesn't block indefinitely waiting for more data if an exception occurs during tile loading
                tileReadWrite.LoadedTiles.CompleteAdding();
            }
        }

        protected void WaitForTasks(string cmdletName, Task[] tasks, LasTileGrid lasGrid, TileReadWrite tileReadWrite)
        {
            ProgressRecord readWriteProgress = new(0, cmdletName, tileReadWrite.TilesLoaded + " tiles read, " + tileReadWrite.TilesWritten + " tiles written of " + lasGrid.NonNullCells + " tiles...");
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

                float fractionComplete = (float)tileReadWrite.TilesWritten / (float)lasGrid.NonNullCells;
                readWriteProgress.StatusDescription = tileReadWrite.TilesLoaded + " tiles read, " + tileReadWrite.TilesWritten + " tiles written of " + lasGrid.NonNullCells + " tiles...";
                readWriteProgress.PercentComplete = (int)(100.0F * fractionComplete);
                readWriteProgress.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(tileReadWrite.Stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                this.WriteProgress(readWriteProgress);
            }

            tileReadWrite.Stopwatch.Stop();
        }

        protected void WriteTiles<TTile>(Func<string, TTile, TileReadWrite<TTile>, int> writeTile, TileReadWrite<TTile> tileReadWrite) where TTile : Grid
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
                    throw new TaskCanceledException("Failed to write tile '" + tileName + "' with extent (" + dsmTilePointZ.GetExtentString() + ").", exception, tileReadWrite.CancellationTokenSource.Token);
                }

                if (this.Stopping || tileReadWrite.CancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
