using Mars.Clouds.Cmdlets.Hardware;
using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    public abstract class LasFilesCmdlet : FileCmdlet
    {
        [Parameter(HelpMessage = "Maximum number of threads to use when processing input files (or other) data in parallel. Default is cmdlet specific but is most commonly the number of physical cores.")]
        [ValidateRange(1, Constant.DefaultMaximumThreads)] // arbitrary upper bound
        public int DataThreads { get; set; }

        [Parameter(HelpMessage = "Whether or not to ignore cases where a .las file's header indicates more variable length records (VLRs) than fit between the header and point data. Default is false but this is a common issue with .las files, particularly a two byte fragment between the last VLR and the start of the points.")]
        public SwitchParameter DiscardOverrunningVlrs { get; set; }

        [Parameter(HelpMessage = "Maximum number of threads to use when reading file metadata in parallel. Default is potentially cmdlet specific but is usually the number of threads supported by the processor.")]
        [ValidateRange(1, Constant.DefaultMaximumThreads)] // arbitrary upper bound
        public int MetadataThreads { get; set; }

        [Parameter(HelpMessage = "Fallback date to use if a .las file's header is missing year or day of year information.")]
        public DateOnly? FallbackDate { get; set; }

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "Input point clouds to process. Can be a single file, list of files, wildcards can be used (*, ?) and, if a directory is indicated, all .las files in the directory will be read.")]
        [ValidateNotNullOrEmpty]
        public List<string> Las { get; set; }

        [Parameter(HelpMessage = $"Number of threads, out of -{nameof(GdalCmdlet.DataThreads)}, to use for reading tiles. Default is automatic estimation, which will typically choose single read thread.")]
        [ValidateRange(1, 32)] // arbitrary upper bound
        public int ReadThreads { get; set; }

        protected LasFilesCmdlet()
        {
            this.DataThreads = HardwareCapabilities.Current.PhysicalCores;
            this.DiscardOverrunningVlrs = false;
            this.FallbackDate = null;
            this.Las = [];
            this.MetadataThreads = Environment.ProcessorCount; // actual supported thread count
            this.ReadThreads = -1;
        }

        protected int GetPointCloudReadThreadCount(float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs)
        {
            return LasFilesCmdlet.GetPointCloudReadThreadCount(this.Las, this.SessionState.Path.CurrentLocation.Path, this.ReadThreads, this.DataThreads, driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs, minWorkerThreadsPerReadThread: 0);
        }

        public static int GetPointCloudReadThreadCount(List<string> cloudPaths, string currentLocation, int maxReadThreads, int maxDataThreads, float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs, int minWorkerThreadsPerReadThread)
        {
            if (maxReadThreads != -1)
            {
                if (maxReadThreads > maxDataThreads)
                {
                    throw new ArgumentOutOfRangeException(nameof(maxReadThreads), $"{nameof(maxReadThreads)} is {maxReadThreads} which exceeds the maximum of {nameof(maxDataThreads)} threads. Set -{nameof(maxReadThreads)} and {nameof(maxDataThreads)} such that the number of read threads is less than or equal to the maximum number of threads.");
                }
                return maxReadThreads; // nothing to do as user's specified the number of read threads
            }

            if (maxDataThreads < 1)
            {
                throw new InvalidOperationException($"{nameof(maxDataThreads)} is {maxDataThreads}. At least one data thread must be allowed. Is the caller failing to assign a default value to {nameof(maxDataThreads)} when it is not user specified?");
            }
            int driveBasedReadThreadEstimate = HardwareCapabilities.Current.GetPracticalReadThreadCount(cloudPaths, currentLocation, driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs);
            return Int32.Min(driveBasedReadThreadEstimate, maxDataThreads / (1 + minWorkerThreadsPerReadThread));
        }
    }
}
