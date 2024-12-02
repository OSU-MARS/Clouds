using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.OGR;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsData.Convert, "CloudCrs")]
    public class ConvertCloudCrs : GdalCmdlet
    {
        private readonly CancellationTokenSource cancellationTokenSource;

        [Parameter(Mandatory = true, HelpMessage = "Point clouds to reproject to a new coordinate system.")]
        [ValidateNotNullOrEmpty]
        public List<string> Las { get; set; }

        [Parameter(HelpMessage = "EPSG of projected coordinate system to assign to point cloud. Default is 32610 (WGS 84 / UTM zone 10N).")]
        [ValidateRange(1024, 32767)]
        public int HorizontalEpsg { get; set; }

        [Parameter(HelpMessage = "EPSG of vertical coordinate system to assign to point cloud. Default is 5703 (NAVD88 meters). If an input cloud does not have a vertical coordinate system it is assumed its z values are in NAVD88 with the same units as its horizontal coordinate system.")]
        [ValidateRange(1024, 32767)]
        public int VerticalEpsg { get; set; }

        public ConvertCloudCrs() 
        {
            this.cancellationTokenSource = new();

            this.Las = [];
            this.HorizontalEpsg = Constant.Epsg.Utm10N;
            this.VerticalEpsg = Constant.Epsg.Navd88m;
        }

        protected override void ProcessRecord()
        {
            // check for input files
            List<string> cloudPaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);

            // create coordinate system to reproject to
            SpatialReference horizontalCrs = new(String.Empty);
            horizontalCrs.ImportFromEpsg(this.HorizontalEpsg);
            SpatialReference verticalCrs = new(String.Empty);
            verticalCrs.ImportFromEpsg(this.VerticalEpsg);
            SpatialReference newCrs = SpatialReferenceExtensions.Create(horizontalCrs, verticalCrs);

            // set point clouds' origins, coordinate systems, and source IDs
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasWriter.GetPointCopyEditBandwidth();
            int readThreads = Int32.Min(HardwareCapabilities.Current.GetPracticalReadThreadCount(this.Las, this.SessionState.Path.CurrentLocation.Path, driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs), this.DataThreads);

            int cloudReprojectionsInitiated = -1;
            int cloudReprojectionsCompleted = 0;
            ParallelTasks cloudRegistrationTasks = new(Int32.Min(readThreads, cloudPaths.Count), () =>
            {
                for (int cloudIndex = Interlocked.Increment(ref cloudReprojectionsInitiated); cloudIndex < cloudPaths.Count; cloudIndex = Interlocked.Increment(ref cloudReprojectionsInitiated))
                {
                    // load cloud and get its current coordinate system
                    string cloudPath = cloudPaths[cloudIndex];
                    using LasReader reader = LasReader.CreateForPointRead(cloudPath);
                    LasFile cloud = new(reader, fallbackCreationDate: null);
                    SpatialReference cloudCrs = cloud.GetSpatialReference();
                    int cloudHasVerticalCrs = cloudCrs.IsVertical();
                    if (SpatialReferenceExtensions.IsSameCrs(cloudCrs, newCrs) && (cloudHasVerticalCrs == 1)) // IsSameCrs() allows missing vertical CRSes
                    {
                        // TODO: pass message back to main cmdlet thread indicating cloud was skipped
                        continue; // nothing to do as cloud is already in desired coordinate system
                    }
                    if (cloudHasVerticalCrs == 0)
                    {
                        // if cloud is missing a vertical CRS assume its vertical units are the same as those of -VerticalEpsg
                        // Otherwise vertical aspects of the coordinate system transform are undefined.
                        cloudCrs = SpatialReferenceExtensions.CreateCompoundCrs(cloudCrs, verticalCrs);
                    }

                    // change cloud's coordinate system
                    cloud.SetSpatialReference(newCrs);

                    // transform cloud's origin
                    CoordinateTransformation transform = new(cloudCrs, newCrs, new());
                    Geometry origin = new(wkbGeometryType.wkbPoint25D);
                    origin.AddPoint(cloud.Header.XOffset, cloud.Header.YOffset, cloud.Header.ZOffset);
                    if (origin.Transform(transform) != 0)
                    {
                        throw new ParameterOutOfRangeException(nameof(this.HorizontalEpsg), "Could not transform point cloud origin " + cloud.Header.XOffset + ", " + cloud.Header.YOffset + ", " + cloud.Header.ZOffset + " to EPSG:" + this.HorizontalEpsg + ".");
                    }
                    double[] newOriginXyz = new double[3];
                    origin.GetPoint(0, newOriginXyz);
                    cloud.SetOrigin(newOriginXyz[0], newOriginXyz[1], newOriginXyz[2]);

                    // update scale factors and extent if linear units have changed
                    double scaleFactorChange = cloudCrs.GetLinearUnits() / newCrs.GetLinearUnits();
                    if (scaleFactorChange != 1.0)
                    {
                        double xOrigin = cloud.Header.XOffset;
                        cloud.Header.MaxX = scaleFactorChange * (cloud.Header.MaxX - xOrigin);
                        cloud.Header.MinX = scaleFactorChange * (cloud.Header.MinX - xOrigin);

                        double yOrigin = cloud.Header.YOffset;
                        cloud.Header.MaxY = scaleFactorChange * (cloud.Header.MaxY - yOrigin);
                        cloud.Header.MinY = scaleFactorChange * (cloud.Header.MinY - yOrigin);

                        double zOrigin = cloud.Header.ZOffset;
                        cloud.Header.MaxZ = scaleFactorChange * (cloud.Header.MaxZ - zOrigin);
                        cloud.Header.MinZ = scaleFactorChange * (cloud.Header.MinZ - zOrigin);

                        cloud.Header.XScaleFactor *= scaleFactorChange;
                        cloud.Header.YScaleFactor *= scaleFactorChange;
                        cloud.Header.ZScaleFactor *= scaleFactorChange;
                    }

                    // write out copy of cloud with reprojected origin, scale, and coordinate system
                    string modifiedCloudPath = PathExtensions.AppendToFileName(cloudPath, " reprojected");
                    using LasWriter writer = LasWriter.CreateForPointWrite(modifiedCloudPath);
                    writer.WriteHeader(cloud);
                    writer.WriteVariableLengthRecordsAndUserData(cloud);
                    writer.CopyPointsAndExtendedVariableLengthRecords(reader, cloud);

                    Interlocked.Increment(ref cloudReprojectionsCompleted);
                    if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }, this.cancellationTokenSource);

            TimedProgressRecord progress = new("Register-Cloud", "Reprojected " + cloudReprojectionsCompleted + " of " + cloudPaths.Count + " point clouds...");
            while (cloudRegistrationTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = "Registered " + cloudReprojectionsCompleted + " of " + cloudPaths.Count + " point clouds...";
                progress.Update(cloudReprojectionsCompleted, cloudPaths.Count);
                this.WriteProgress(progress);
            }

            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            this.cancellationTokenSource.Cancel();
            base.StopProcessing();
        }
    }
}
