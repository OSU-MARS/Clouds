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
        public string Cells { get; set; }

        protected LasTilesToRasterCmdlet() 
        {
            this.Cells = String.Empty;
        }
    }
}
