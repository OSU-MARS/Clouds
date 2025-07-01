using Mars.Clouds.Extensions;
using OSGeo.GDAL;
using OSGeo.OGR;
using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "LayerCounts")]
    public class GetLayerCounts : FileCmdlet
    {
        private readonly CancellationTokenSource cancellationTokenSource;

        [Parameter(Mandatory = true, HelpMessage = "List of directories or wildcarded file paths to read files from.")]
        [ValidateNotNullOrWhiteSpace]
        public List<string> Files { get; set; }

        [Parameter(HelpMessage = "Maximum number of threads to use when reading layer metadata. Default is the number of concurrent threads supported by the processor.")]
        [ValidateRange(1, Constant.DefaultMaximumThreads)] // arbitrary upper bound
        public int MetadataThreads { get; set; }

        public SwitchParameter Force { get; set; }

        public GetLayerCounts()
        {
            this.cancellationTokenSource = new CancellationTokenSource();
            this.Files = [];
            this.MetadataThreads = Environment.ProcessorCount;
        }

        protected override void ProcessRecord()
        {
            List<string> filePathsToRead = this.GetExistingFilePaths(this.Files, Constant.File.GeoPackageExtension);

            int fileReadIndex = -1;
            ParallelTasks fileTasks = new(Int32.Min(filePathsToRead.Count, this.MetadataThreads), () =>
            {
                int fileIndex = Interlocked.Increment(ref fileReadIndex);
                string filePath = filePathsToRead[fileIndex];
                using Dataset dataset = Gdal.Open(filePath, Access.GA_ReadOnly);
                int layers = dataset.GetLayerCount();
                for (int layerIndex = 0; layerIndex < layers; ++layerIndex)
                {
                    int gdalLayerIndex = layerIndex + 1;
                    Layer layer = dataset.GetLayer(gdalLayerIndex);
                    string layerName = layer.GetName();
                    layer.GetFeatureCount(force: 0);
                }
            }, this.cancellationTokenSource);

            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            this.cancellationTokenSource.Cancel();
            base.StopProcessing();
        }
    }
}
