using Mars.Clouds.Cmdlets;
using Mars.Clouds.Extensions;
using Mars.Clouds.Las;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Management.Automation;

namespace Mars.Clouds
{
    [Cmdlet(VerbsCommon.Get, "PointClassifications")]
    public class GetPointClassifications : LasFilesCmdlet
    {
        public override string GetName()
        {
            return $"{VerbsCommon.Get}-PointClassifications";
        }

        protected override void ProcessRecord()
        {
            List<string> cloudPaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);

            // estimate read thread count
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetPointsToClassificationBandwidth();
            int readThreads = Int32.Min(this.GetPointCloudReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs), cloudPaths.Count);

            FileRead pointCountRead = new();
            List<PointCountsByClassification> cloudPointCounts = new(cloudPaths.Count);

            ParallelTasks pointCountTasks = new(readThreads, () =>
            {
                byte[]? pointReadBuffer = null;
                for (int cloudIndex = pointCountRead.GetNextFileReadIndexThreadSafe(); cloudIndex < cloudPaths.Count; cloudIndex = pointCountRead.GetNextFileReadIndexThreadSafe())
                {
                    string cloudPath = cloudPaths[cloudIndex];
                    using LasReader reader = LasReader.CreateForPointRead(cloudPath, this.DiscardOverrunningVlrs);
                    LasFile lasFile = new(cloudPath, reader, this.FallbackDate);

                    PointCountsByClassification pointCountsByClassification = new(cloudPath);
                    reader.ReadPointsToClassification(lasFile, pointCountsByClassification, ref pointReadBuffer);
                    pointCountRead.IncrementFilesReadThreadSafe();

                    lock (cloudPointCounts)
                    {
                        cloudPointCounts.Add(pointCountsByClassification);
                    }

                    if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                    {
                        return; // if cmdlet's been stopped with ctrl+c the read semaphore may already be disposed
                    }
                }
            }, this.CancellationTokenSource);

            TimedProgressRecord progress = new(this.GetName(), pointCountRead.GetPointCloudReadStatusDescription(cloudPaths.Count, pointCountTasks.Count));
            this.WriteProgress(progress);
            while (pointCountTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = pointCountRead.GetPointCloudReadStatusDescription(cloudPaths.Count, pointCountTasks.Count);
                progress.Update(pointCountRead.FilesRead, cloudPaths.Count);
                this.WriteProgress(progress);
            }

            if (cloudPointCounts.Count == 1)
            {
                this.WriteObject(cloudPointCounts[0]);
            }
            else
            {
                this.WriteObject(cloudPointCounts);
            }
            base.ProcessRecord();
        }
    }
}
