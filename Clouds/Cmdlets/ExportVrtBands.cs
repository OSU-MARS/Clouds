using Mars.Clouds.Vrt;
using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsData.Export, "VrtBands")]
    public class ExportVrtBands : Cmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "List of bands to include in the virtual raster. All available bands are included if not specified.")]
        [ValidateNotNullOrEmpty]
        public List<string> Bands { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path of .vrt file to export bands from.")]
        [ValidateNotNullOrEmpty]
        public string Input { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path of .vrt file to generate.")]
        [ValidateNotNullOrEmpty]
        public string Output { get; set; }

        public ExportVrtBands() 
        {
            this.Bands = [];
            this.Input = String.Empty; 
            this.Output = String.Empty;
        }

        protected override void ProcessRecord()
        {
            // filter bands in virtual raster
            VrtDataset vrt = new(this.Input);
            if (vrt.Bands.Count == 0)
            {
                throw new ParameterOutOfRangeException(nameof(this.Input), $"No are present in virtual raster '{this.Input}'. Output .vrt would also be empty.");
            }

            int bandsRemoved = 0;
            for (int bandIndex = 0; bandIndex < vrt.Bands.Count; ++bandIndex) 
            { 
                while (this.Bands.Contains(vrt.Bands[bandIndex].Description) == false)
                {
                    vrt.Bands.RemoveAt(bandIndex);
                    ++bandsRemoved;

                    if (vrt.Bands.Count <= bandIndex)
                    {
                        break; // no more bands to remove
                    }
                }
            }

            if (bandsRemoved == 0)
            {
                throw new ParameterOutOfRangeException(nameof(this.Bands), $"No bands were removed from virtual raster so output .vrt would be the same as the input. Are -{nameof(this.Bands)} and -{nameof(this.Input)} correct?");
            }
            if (vrt.Bands.Count == 0)
            {
                throw new ParameterOutOfRangeException(nameof(this.Bands), $"No bands were retained in the input virtual raster so output .vrt would be empty. Are -{nameof(this.Bands)} and -{nameof(this.Input)} correct?");
            }

            // update virtual raster band numbering
            for (int bandIndex = 0; bandIndex < vrt.Bands.Count; ++bandIndex)
            {
                vrt.Bands[bandIndex].Band = bandIndex + 1;
            }

            // write .vrt with reduced set of bands
            vrt.WriteXml(this.Output);
            base.ProcessRecord();
        }
    }
}
