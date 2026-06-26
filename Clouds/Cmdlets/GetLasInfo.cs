using Mars.Clouds.Las;
using System.Collections.Generic;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "LasInfo")]
    public class GetLasInfo : LasFilesCmdlet
    {
        public override string GetName()
        {
            return $"{VerbsCommon.Get}-LasInfo";
        }

        protected override void ProcessRecord()
        {
            List<string> cloudPaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);
            List<LasFile> cloudMetadata = new(cloudPaths.Count);
            for (int fileIndex = 0; fileIndex < cloudPaths.Count; ++fileIndex)
            {
                // can multithread if needed but doesn't seem worth the overhead up to a few hundred .las files
                string lasFilePath = cloudPaths[fileIndex];
                using LasReader lasReader = LasReader.CreateForHeaderAndVlrRead(lasFilePath, this.DiscardOverrunningVlrs);
                LasFile lasFile = new(lasFilePath, lasReader, this.FallbackDate);
                cloudMetadata.Add(lasFile);

                if (this.Stopping)
                {
                    return;
                }
            }

            if (cloudMetadata.Count == 1)
            {
                this.WriteObject(cloudMetadata[0]);
            }
            else
            {
                this.WriteObject(cloudMetadata);
            }
            base.ProcessRecord();
        }
    }
}
