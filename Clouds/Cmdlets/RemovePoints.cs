using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.Las;
using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Remove, "Points")]
    public class RemovePoints : LasCmdlet
    {
        private CancellationTokenSource? cancellationTokenSource;

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Output locations of filtered point clouds.")]
        [ValidateNotNullOrEmpty]
        public string Filtered { get; set; }

        public RemovePoints() 
        {
            this.cancellationTokenSource = null;
            this.Filtered = String.Empty;
        }

        protected override void ProcessRecord()
        {
            // assemble inputs
            List<string> sourceCloudPaths = FileCmdlet.GetExistingFilePaths(this.Las, Constant.File.LasExtension);
            if (sourceCloudPaths.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.Las), "-" + nameof(this.Las) + " = '" + String.Join(", ", this.Las) + "' does not match any existing point clouds.");
            }

            bool outputPathIsDirectory = Directory.Exists(this.Filtered);
            if ((sourceCloudPaths.Count > 1) && (outputPathIsDirectory == false))
            {
                throw new ParameterOutOfRangeException(nameof(this.Filtered), "-" + nameof(this.Filtered) + " must be an existing directory when -" + nameof(this.Filtered) + " indicates multiple files.");
            }

            // remove points from clouds
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasWriter.GetPointCopyEditBandwidth();
            HardwareCapabilities hardwareCapabilities = HardwareCapabilities.Current;
            int readThreads = Int32.Min(hardwareCapabilities.GetPracticalReadThreadCount(this.Las, driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs), this.MaxThreads);

            this.cancellationTokenSource = new();
            int cloudFiltrationsInitiated = -1;
            int cloudFiltrationsCompleted = 0;
            UInt64 pointsRemoved = 0;
            ParallelTasks pointFilterTasks = new(readThreads, () =>
            {
                for (int cloudIndex = Interlocked.Increment(ref cloudFiltrationsInitiated); cloudIndex < sourceCloudPaths.Count; cloudIndex = Interlocked.Increment(ref cloudFiltrationsInitiated))
                {
                    string sourceCloudPath = sourceCloudPaths[cloudIndex];
                    string? sourceCloudName = Path.GetFileName(sourceCloudPath);
                    if (String.IsNullOrWhiteSpace(sourceCloudName))
                    {
                        throw new NotSupportedException("Could not find a point cloud file name in path '" + sourceCloudPath + "'.");
                    }
                    string destinationCloudPath = outputPathIsDirectory ? Path.Combine(this.Filtered, sourceCloudName) : this.Filtered;

                    using LasReader reader = LasReader.CreateForPointRead(sourceCloudPath, this.DiscardOverrunningVlrs);
                    LasFile cloud = new(reader, fallbackCreationDate: null);

                    using LasWriter writer = LasWriter.CreateForPointWrite(destinationCloudPath);
                    writer.WriteHeader(cloud);
                    writer.WriteVariableLengthRecordsAndUserData(cloud);
                    LasFilteringResult nonNoisePoints = writer.CopyNonNoisePoints(reader, cloud);
                    writer.WriteExtendedVariableLengthRecords(cloud);

                    // TODO: should bounding box be updated?
                    // For tiles, x and y extents probably shouldn't change. But if high and low noise are removed it's likely z extents
                    // contract.
                    cloud.Header.SetNumberOfPointsByReturn(nonNoisePoints.NumberOfPointsByReturn);
                    cloud.Header.MaxZ = nonNoisePoints.ZMax;
                    cloud.Header.MinZ = nonNoisePoints.ZMin;
                    writer.WriteHeader(cloud); // seeks back to start of stream, arguably more elegant to write only modified bytes but it doesn't appear worth the complexity of implementing a separate serialization path

                    Interlocked.Increment(ref cloudFiltrationsCompleted);
                    Interlocked.Add(ref pointsRemoved, nonNoisePoints.PointsRemoved);
                }
            }, this.cancellationTokenSource);

            TimedProgressRecord progress = new("Register-Cloud", "Removed noise and withheld points from " + cloudFiltrationsCompleted + " of " + sourceCloudPaths.Count + " point clouds...");
            while (pointFilterTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = "Removed noise and withheld points from " + cloudFiltrationsCompleted + " of " + sourceCloudPaths.Count + " point clouds...";
                progress.Update(cloudFiltrationsCompleted, sourceCloudPaths.Count);
                this.WriteProgress(progress);
            }

            progress.Stopwatch.Stop();
            this.WriteVerbose("Removed " + pointsRemoved.ToString("n0") + " points from " + sourceCloudPaths.Count + (sourceCloudPaths.Count > 1 ? " clouds in " : " cloud in ") + progress.Stopwatch.ToElapsedString() + ".");
            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            this.cancellationTokenSource?.Cancel();
            base.StopProcessing();
        }
    }
}
