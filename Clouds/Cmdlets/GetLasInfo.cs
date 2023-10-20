using Mars.Clouds.Las;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "LasInfo")]
    public class GetLasInfo : Cmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = ".las or .laz file to retrieve header and variable length records from.")]
        [ValidateNotNullOrEmpty]
        public string? Las { get; set; }

        protected override void ProcessRecord()
        {
            Debug.Assert(this.Las != null);

            using FileStream stream = new(this.Las, FileMode.Open, FileAccess.Read, FileShare.Read);
            using LasReader reader = new(stream);
            LasFile lasFile = new(reader);
            reader.ReadVariableLengthRecords(lasFile);
            reader.Dispose();
            this.WriteObject(lasFile);
        }
    }
}
