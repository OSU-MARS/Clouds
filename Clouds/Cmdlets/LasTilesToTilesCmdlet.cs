using Mars.Clouds.Las;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    public class LasTilesToTilesCmdlet : LasTilesCmdlet
    {
        [Parameter(HelpMessage = "Whether or not to compress output rasters (DSM or orthoimages). Default is false.")]
        public SwitchParameter CompressRasters { get; set; }

        [Parameter(HelpMessage = "Perform all orthoimage generation steps except writing images to disk. Though this switch disables output it allows evaluation of drive thermals without incurring flash write wear and provides insight in certain types performance profiling.")]
        public SwitchParameter NoWrite { get; set; }

        [Parameter(HelpMessage = "Enable use of unbuffered IO on Windows. This is likely to be helpful in utilizing PCIe 4.0 x4 and faster NVMe drives. Default is buffered IO.")]
        public SwitchParameter Unbuffered { get; set; }

        protected LasTilesToTilesCmdlet()
        {
            this.CompressRasters = false;
        }

        protected LasTileGrid ReadLasHeadersAndFormGrid(string cmdletName, string outputParameterName, bool outputPathIsDirectory)
        {
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, requiredEpsg: null);
            if ((lasGrid.NonNullCells > 1) && (outputPathIsDirectory == false))
            {
                throw new ParameterOutOfRangeException(outputParameterName, "-" + outputParameterName + " must be an existing directory when " + nameof(this.Las) + " indicates multiple files.");
            }

            return lasGrid;
        }
    }
}
