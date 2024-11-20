using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    public class LasTilesToRasterCmdlet : LasTilesCmdlet
    {
        [Parameter(HelpMessage = "If specified, a raster defining the grid over which a monolithic grid or scan metrics raster is calculated. Metrics are not calculated for cells outside the point cloud and grid metrics are not calculated for cells with no data values.")]
        [ValidateNotNullOrEmpty]
        public string? Cells { get; set; }

        protected LasTilesToRasterCmdlet() 
        {
            this.Cells = null;
        }
    }
}
