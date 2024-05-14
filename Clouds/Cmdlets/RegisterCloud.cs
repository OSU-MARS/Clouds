using Mars.Clouds.Cmdlets.Drives;
using Mars.Clouds.Extensions;
using Mars.Clouds.Las;
using OSGeo.OGR;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsLifecycle.Register, "Cloud")]
    public class RegisterCloud : GdalCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "Point clouds to set origin and coordinate system of.")]
        [ValidateNotNullOrEmpty]
        public List<string> Las { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Latitude of point cloud's origin in WGS84 coordinates.")]
        [ValidateRange(-90.0, 90.0)]
        public double Lat { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Longitude of point cloud's origin in WGS84 coordinates.")]
        [ValidateRange(-180.0, 180.0)]
        public double Long { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Elevation of point cloud's origin in meters, WGS84.")]
        [ValidateRange(-420.0, 8848.0)]
        public double Z { get; set; }

        [Parameter(HelpMessage = "EPSG number of projected coordinate system to assign to point cloud. Default is 32610 (WGS 84 / UTM zone 10N).")]
        [ValidateRange(1024, 32767)]
        public int HorizontalEpsg { get; set; }

        [Parameter(HelpMessage = "EPSG number of vertical coordinate system to assign to point cloud. Default is 5703 (NAVD88 meters).")]
        [ValidateRange(1024, 32767)]
        public int VerticalEpsg { get; set; }

        [Parameter(HelpMessage = "Initial source ID to assign. Incremented for each file found thereafter.")]
        [ValidateRange(0, UInt16.MaxValue)]
        public int SourceID { get; set; }

        [Parameter(HelpMessage = "Fallback date to use if .las header is missing year or day of year information.")]
        public DateOnly? FallbackDate { get; set; }

        public RegisterCloud()
        {
            this.Las = [];
            this.Lat = Double.NaN;
            this.Long = Double.NaN;
            this.Z = Double.NaN;
            this.HorizontalEpsg = Constant.Epsg.Utm10N;
            this.VerticalEpsg = Constant.Epsg.Navd88m;
            this.SourceID = 1;
        }

        protected override void BeginProcessing()
        {
            // check for input files
            List<string> cloudPaths = FileCmdlet.GetExistingFilePaths(this.Las, Constant.File.LasExtension);

            // reproject point cloud origin from WGS84 to cloud's coordinate system
            SpatialReference wgs84 = new(String.Empty);
            wgs84.ImportFromEPSG(Constant.Epsg.Wgs84);
            
            SpatialReference horizontalCrs = new(String.Empty);
            if (horizontalCrs.ImportFromEPSG(this.HorizontalEpsg) != 0)
            {
                throw new ParameterOutOfRangeException(nameof(this.HorizontalEpsg), "Could not create a GDAL spatial reference from -" + nameof(this.HorizontalEpsg) + " " + this.HorizontalEpsg + ".");
            }
            SpatialReference verticalCrs = new(String.Empty);
            if (verticalCrs.ImportFromEPSG(this.VerticalEpsg) != 0)
            {
                throw new ParameterOutOfRangeException(nameof(this.VerticalEpsg), "Could not create a GDAL spatial reference from -" + nameof(this.VerticalEpsg) + " " + this.VerticalEpsg + ".");
            }
            SpatialReference cloudCrs = new(String.Empty);
            if (cloudCrs.SetCompoundCS(horizontalCrs.GetName() + " + " + verticalCrs.GetName(), horizontalCrs, verticalCrs) != 0)
            {
                throw new ParameterOutOfRangeException(nameof(this.VerticalEpsg), "Could not create a compound GDAL spatial reference from -" + nameof(this.HorizontalEpsg) + " " + this.HorizontalEpsg + " and -" + nameof(this.VerticalEpsg) + " " + this.VerticalEpsg + ".");
            }

            CoordinateTransformation transform = new(wgs84, cloudCrs, new());
            Geometry origin = new(wkbGeometryType.wkbPoint25D);
            origin.AddPoint(this.Lat, this.Long, this.Z); // GDAL reverses x and y for WGS84
            if (origin.Transform(transform) != 0)
            {
                throw new ParameterOutOfRangeException(nameof(this.HorizontalEpsg), "Could not transform point cloud origin " + this.Lat + ", " + this.Long + ", " + this.Z + " to EPSG:" + this.HorizontalEpsg + ".");
            }
            double[] originXyz = new double[3];
            origin.GetPoint(0, originXyz);

            // set point clouds' origins, coordinate systems, and source IDs
            DriveCapabilities driveCapabilities = DriveCapabilities.Create(this.Las);
            int readThreads = Int32.Min(driveCapabilities.GetPracticalThreadCount(LasWriter.RegisterSpeedInGBs), this.MaxThreads);

            int cloudReadsInitiated = -1;
            int cloudReadsCompleted = 0;
            ParallelTasks cloudRegistrationTasks = new(Int32.Min(readThreads, cloudPaths.Count), () =>
            {
                for (int cloudIndex = Interlocked.Increment(ref cloudReadsInitiated); cloudIndex < cloudPaths.Count; cloudIndex = Interlocked.Increment(ref cloudReadsInitiated))
                {
                    string cloudPath = cloudPaths[cloudIndex];
                    FileInfo cloudFileInfo = new(cloudPath);
                    using LasReader reader = LasReader.CreateForPointRead(cloudPath, cloudFileInfo.Length);
                    LasFile cloud = new(reader, this.FallbackDate); // leaves reader positioned at start of points
                    cloud.SetOrigin(originXyz);
                    cloud.SetSpatialReference(cloudCrs);

                    string modifiedCloudPath = PathExtensions.AppendToFileName(cloudPath, " registered");
                    using LasWriter writer = LasWriter.CreateForPointWrite(modifiedCloudPath);
                    writer.WriteHeader(cloud);
                    writer.WriteVariableLengthRecords(cloud);
                    writer.WritePointsWithSourceID(reader, cloud, (UInt16)(this.SourceID + cloudIndex));

                    Interlocked.Increment(ref cloudReadsCompleted);
                    if (this.Stopping)
                    {
                        break;
                    }
                }
            });

            TimedProgressRecord progress = new("Register-Cloud", "Registered " + cloudReadsCompleted + " of " + cloudPaths.Count + " point clouds...");
            while (cloudRegistrationTasks.WaitAll(Constant.DefaultProgressInterval) == false) 
            {
                progress.StatusDescription = "Registered " + cloudReadsCompleted + " of " + cloudPaths.Count + " point clouds...";
                progress.Update(cloudReadsCompleted, cloudPaths.Count);
                this.WriteProgress(progress);
            }

            base.BeginProcessing();
        }
    }
}
