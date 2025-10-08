using Mars.Clouds.DiskSpd;
using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsData.Convert, "DiskSpd")]
    public class ConvertDiskSpd : FileCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "List of DiskSpd XML log files to read. Wildcards can be used and, if a directory is indicated, all .xml files in the directory will be read.")]
        [ValidateNotNullOrEmpty]
        public List<string> Result { get; set; }

        [Parameter(Mandatory = true, HelpMessage = $"Path to output .xlsx containing one row from each thread of the .xml results files indicated by -{nameof(this.Result)}.")]
        [ValidateNotNullOrEmpty]
        public string Longform { get; set; }

        public ConvertDiskSpd()
        {
            this.Result = [];
            this.Longform = String.Empty;
        }

        protected override void ProcessRecord()
        {
            List<string> logFiles = this.GetExistingFilePaths(this.Result, Constant.File.XmlExtension);
            List<Results> resultsByFile = new(logFiles.Count);
            for (int fileIndex = 0; fileIndex < logFiles.Count; ++fileIndex)
            {
                Results results = new(logFiles[fileIndex]);
                resultsByFile.Add(results);

                if (this.Stopping)
                {
                    return;
                }
            }

            DiskSpdLongformResults longformResults = new(resultsByFile);
            using FileStream stream = new(this.Longform, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, Constant.File.DefaultBufferSize);
            longformResults.Write(stream);

            base.ProcessRecord();
        }
    }
}
