using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.OGR;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "BoundingBoxes")]
    public class GetBoundingBoxes : GdalCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "Point clouds whose bounding boxes will be placed in the output bounding box layer.")]
        [ValidateNotNullOrEmpty]
        public List<string> Las { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path to file containing the generated bounding box layer.")]
        [ValidateNotNullOrWhiteSpace]
        public string Bounds { get; set; }

        [Parameter(HelpMessage = "Name of bounding box layer to create in the file indicated by -Bounds. Default is 'point cloud bounding boxes'.")]
        [ValidateNotNullOrWhiteSpace]
        public string BoundsLayer { get; set; }

        [Parameter(HelpMessage = "Maximum length of bounding box names in the bounding box layer. Default is 16 characters. Names are the first n characters of the point clouds' file names (excluding the extension).")]
        public int NameLength { get; set; }

        public GetBoundingBoxes()
        {
            this.Bounds = String.Empty;
            this.BoundsLayer = "point cloud bounding boxes";
            this.Las = [];
            this.NameLength = 16;
        }

        protected override void ProcessRecord()
        {
            List<string> cloudPaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);

            string boundsFilePath = this.GetRootedPath(this.Bounds);
            using DataSource boundingBoxFile = OgrExtensions.CreateOrOpenForWrite(boundsFilePath);
            SpatialReference? boundingBoxCrs = null;
            BoundingBoxLayer? boundingBoxLayer = null;

            int boundingBoxReadsInitiated = -1;
            int boundingBoxReadsCompleted = 0;
            ParallelTasks boundsTasks = new(Int32.Min(this.MetadataThreads, cloudPaths.Count), () =>
            {
                for (int cloudIndex = Interlocked.Increment(ref boundingBoxReadsInitiated); cloudIndex < cloudPaths.Count; cloudIndex = Interlocked.Increment(ref boundingBoxReadsInitiated))
                {
                    // load cloud and get its current coordinate system
                    string cloudPath = cloudPaths[cloudIndex];
                    using LasReader reader = LasReader.CreateForPointRead(cloudPath);
                    LasFile cloud = new(reader, fallbackCreationDate: null);

                    SpatialReference cloudCrs = cloud.GetSpatialReference();
                    if (boundingBoxLayer == null)
                    {
                        lock (boundingBoxFile)
                        {
                            if (boundingBoxLayer == null)
                            {
                                boundingBoxCrs = cloudCrs;
                                boundingBoxLayer = BoundingBoxLayer.CreateOrOverwrite(boundingBoxFile, this.BoundsLayer, boundingBoxCrs, nameField: "name", this.NameLength);
                            }
                        }
                    }
                    else 
                    {
                        Debug.Assert(boundingBoxCrs != null);
                        if (SpatialReferenceExtensions.IsSameCrs(cloudCrs, boundingBoxCrs) == false)
                        {
                            throw new NotSupportedException("Point cloud '" + cloudPath + "'s coordinate system ('" + cloudCrs.GetName() + "') does not match the bounding box layer's coordinate system ('" + boundingBoxCrs.GetName() + "').");
                        }
                    }

                    string cloudName = Tile.GetName(cloudPath);
                    if (cloudName.Length > this.NameLength)
                    {
                        cloudName = cloudName[..this.NameLength];
                    }
                    lock (boundingBoxLayer)
                    {
                        boundingBoxLayer.Add(cloudName, cloud.Header.MinX, cloud.Header.MinY, cloud.Header.MaxX, cloud.Header.MaxY);
                    }

                    if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }, this.CancellationTokenSource);

            TimedProgressRecord progress = new("Get-BoundingBoxes", "Read " + boundingBoxReadsCompleted + " of " + cloudPaths.Count + " bounding boxes...");
            while (boundsTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = "Read " + boundingBoxReadsCompleted + " of " + cloudPaths.Count + " bounding boxes...";
                progress.Update(boundingBoxReadsCompleted, cloudPaths.Count);
                this.WriteProgress(progress);
            }

            if (boundingBoxLayer != null)
            {
                progress.StatusDescription = "Waiting for GDAL to finish committing treetops to '" + Path.GetFileName(this.Bounds) + "'...";
                progress.PercentComplete = 0;
                progress.SecondsRemaining = -1;
                this.WriteProgress(progress);

                boundingBoxLayer.Dispose();
                boundingBoxFile.FlushCache(); // boundingBoxFile is a using variable, so will always be disposed
            }

            base.ProcessRecord();
        }
    }
}
