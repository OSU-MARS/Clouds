using Mars.Clouds.Las;
using System.Collections.Generic;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "LasInfo")]
    public class GetLasInfo : LasCmdlet
    {
        protected override void ProcessRecord()
        {
            List<string> lasFilePaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);
            List<LasTile> lasFileMetadata = new(lasFilePaths.Count); // use LasTile to flow file paths
            for (int fileIndex = 0; fileIndex < lasFilePaths.Count; ++fileIndex)
            {
                // can multithread if needed but doesn't seem worth the overhead up to a few hundred .las files
                string lasFilePath = lasFilePaths[fileIndex];
                LasReader lasReader = LasReader.CreateForHeaderAndVlrRead(lasFilePath, this.DiscardOverrunningVlrs);
                LasTile lasFile = new(lasFilePath, lasReader, fallbackCreationDate: null);
                lasFileMetadata.Add(lasFile);

                if (this.Stopping)
                {
                    return;
                }
            }

            if (lasFileMetadata.Count == 1)
            {
                this.WriteObject(lasFileMetadata[0]);
            }
            else
            {
                this.WriteObject(lasFileMetadata);
            }
            base.ProcessRecord();
        }
    }
}
