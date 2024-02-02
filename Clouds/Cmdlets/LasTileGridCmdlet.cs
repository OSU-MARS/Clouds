using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    public class LasTileGridCmdlet : LasTileCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "Raster with a band defining the grid over which point cloud metrics are calculated. Metrics are not calculated for cells outside the point cloud or which have a no data value in the first band. The raster must be in the same CRS as the point cloud tiles specified by -Las.")]
        [ValidateNotNullOrEmpty]
        public string? Cells { get; set; }

        protected LasTileGridCmdlet() 
        {
            // this.Cells is mandatory
        }
    }
}
