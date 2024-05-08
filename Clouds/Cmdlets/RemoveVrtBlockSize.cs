using Mars.Clouds.Vrt;
using System;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Remove, "VrtBlockSize")]
    public class RemoveVrtBlockSize : Cmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "Path of .vrt to remove BlockXSize and BlockYSize attributes from.")]
        [ValidateNotNullOrEmpty]
        public string Vrt { get; set; }

        public RemoveVrtBlockSize() 
        {
            this.Vrt = String.Empty;
        }

        protected override void ProcessRecord()
        {
            // set every source's block x and y sizes to no data
            VrtDataset vrt = new(this.Vrt);
            for (int bandIndex = 0; bandIndex < vrt.Bands.Count; ++bandIndex)
            {
                VrtRasterBand band = vrt.Bands[bandIndex];
                for (int sourceIndex = 0; sourceIndex < band.Sources.Count; ++sourceIndex) 
                {
                    ComplexSource source = band.Sources[sourceIndex];
                    source.SourceProperties.BlockXSize = UInt32.MaxValue;
                    source.SourceProperties.BlockYSize = UInt32.MaxValue;
                }
            }

            // write revised .vrt
            vrt.WriteXml(this.Vrt);
            base.ProcessRecord();
        }
    }
}
