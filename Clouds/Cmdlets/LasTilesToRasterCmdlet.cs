using Mars.Clouds.Las;
using System;
using System.Management.Automation;
using System.Threading.Tasks;

namespace Mars.Clouds.Cmdlets
{
    public class LasTilesToRasterCmdlet : LasTilesCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "Raster with a band defining the grid over which point cloud metrics are calculated. Metrics are not calculated for cells outside the point cloud or which have a no data value in the first band. The raster must be in the same CRS as the point cloud tiles specified by -Las.")]
        [ValidateNotNullOrEmpty]
        public string? Cells { get; set; }

        protected LasTilesToRasterCmdlet() 
        {
            // this.Cells is mandatory
        }

        protected TimedProgressRecord WaitForTasks(string cmdletName, Task[] tasks, LasTileGrid lasGrid, TileReadToRaster tileRead)
        {
            TimedProgressRecord gridMetricsProgress = new(cmdletName, "Calculating metrics: " + tileRead.RasterCellsCompleted.ToString("#,#,0") + " of " + tileRead.RasterCells.ToString("#,0") + " cells (" + tileRead.TilesRead + " of " + lasGrid.NonNullCells + " point cloud tiles)...");
            this.WriteProgress(gridMetricsProgress);

            while (Task.WaitAll(tasks, LasTilesCmdlet.ProgressUpdateInterval) == false)
            {
                // see remarks in LasTilesToTilesCmdlet.WaitForTasks()
                for (int taskIndex = 0; taskIndex < tasks.Length; ++taskIndex)
                {
                    Task task = tasks[taskIndex];
                    if (task.IsFaulted)
                    {
                        tileRead.CancellationTokenSource.Cancel();
                        throw task.Exception;
                    }
                }

                gridMetricsProgress.StatusDescription = "Calculating metrics: " + tileRead.RasterCellsCompleted.ToString("#,#,0") + " of " + tileRead.RasterCells.ToString("#,0") + " cells (" + tileRead.TilesRead + " of " + lasGrid.NonNullCells + " point cloud tiles)...";
                gridMetricsProgress.Update(tileRead.RasterCellsCompleted, tileRead.RasterCells);
                this.WriteProgress(gridMetricsProgress);
            }

            return gridMetricsProgress;
        }

        protected class TileReadToRaster : TileRead
        {
            public int RasterCells { get; private init; }
            public int RasterCellsCompleted { get; set; }

            public TileReadToRaster(int rasterCells)
            {
                this.RasterCells = rasterCells;
                this.RasterCellsCompleted = 0;
            }
        }
    }
}
