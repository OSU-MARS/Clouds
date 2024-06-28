using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    public class LasCmdlet : FileCmdlet
    {
        [Parameter(HelpMessage = "Whether or not to ignore cases where a .las file's header indicates more variable length records (VLRs) than fit between the header and point data. Default is false but this is a common issue with .las files, particularly a two byte fragment between the last VLR and the start of the points.")]
        public SwitchParameter DiscardOverrunningVlrs { get; set; }

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "Input point clouds to process. Wildcards can be used and, if a directory is indicated, all .las files in the directory will be read.")]
        [ValidateNotNullOrEmpty]
        public List<string> Las { get; set; }

        [Parameter(HelpMessage = "Maximum number of threads to use for processing point clouds in parallel. Default is the procesor's thread count.")]
        public int MaxThreads { get; set; }

        protected LasCmdlet()
        {
            this.DiscardOverrunningVlrs = false;
            this.Las = [];
            this.MaxThreads = Environment.ProcessorCount; // for now, assume IO thread limit is more likely binding
        }
    }
}
