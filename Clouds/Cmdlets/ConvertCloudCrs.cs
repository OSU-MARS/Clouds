using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        [Parameter(HelpMessage = "EPSG of projected coordinate system to assign to the reprojected point cloud. Default is 32610 (WGS 84 / UTM zone 10N).")]
        [ValidateRange(Constant.Epsg.Min, Constant.Epsg.Max)]
        public int HorizontalEpsg { get; set; }

        [Parameter(HelpMessage = "EPSG of vertical coordinate system to assign to the reprojected point cloud. Default is 5703 (NAVD88 meters).")]
        [ValidateRange(Constant.Epsg.Min, Constant.Epsg.Max)]
        public int VerticalEpsg { get; set; }

        [Parameter(HelpMessage = "Scale correction factor to apply input point clouds. Default is 1.0, which has no effect. If a point cloud declares a metric CRS but contains data that's actually in feet, use -InputScale 0.3048. Conversely, if an English CRS is declared but data is actually in meters, use -InputScale 3.28084.")]
        [ValidateRange(0.3048, 3.28084)]
        public double InputScale { get; set; }

        [Parameter(HelpMessage = "EPSG of input point clouds' vertical coordinate system if not specified in the data files. Default is unset, in which case reprojection will fail if an input cloud lacks a vertical CRS. Common choices in the United States are 5703 (NAVD88 meters), 6360 (NAVD88 feet), and 8228 (NAVD88 feet for certain state planes).")]
        [ValidateRange(Constant.Epsg.Min, Constant.Epsg.Max)]
        public int InputVerticalEpsg { get; set; }

        public ConvertCloudCrs() 
        {
            this.cancellationTokenSource = new();

            this.Las = [];
            this.HorizontalEpsg = Constant.Epsg.Utm10N;
            this.InputScale = 1.0;
            this.InputVerticalEpsg = -1;
            this.VerticalEpsg = Constant.Epsg.Navd88m;
        }

        protected override void ProcessRecord()
        {
            // check for input files
            List<string> cloudPaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);

            SpatialReference? inputVerticalCrs = null;
            if (this.InputVerticalEpsg >= Constant.Epsg.Min)
            {
                inputVerticalCrs = SpatialReferenceExtensions.Create(this.InputVerticalEpsg);
            }

            // create coordinate system to reproject to
            SpatialReference newCrs = SpatialReferenceExtensions.CreateCompound(this.HorizontalEpsg, this.VerticalEpsg);

            // set point clouds' origins, coordinate systems, and source IDs
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasWriter.GetPointCopyEditBandwidth();
            int readThreads = Int32.Min(HardwareCapabilities.Current.GetPracticalReadThreadCount(this.Las, this.SessionState.Path.CurrentLocation.Path, driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs), this.DataThreads);

            int cloudReprojectionsInitiated = -1;
            int cloudReprojectionsCompleted = 0;
            ParallelTasks cloudRegistrationTasks = new(Int32.Min(readThreads, cloudPaths.Count), () =>
            {
                double[] newBoundingBox = new double[4]; // can't stackalloc due to GDAL's TransfomBounds() and TransformPoints() signatures
                double[] newXoriginMinMin = new double[3];
                double[] newYoriginMinMax = new double[3];
                double[] newZoriginMinMax = new double[3];
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
                        if (inputVerticalCrs == null)
                        {
                            throw new ParameterOutOfRangeException(nameof(this.InputVerticalEpsg), "Input file '" + cloudPath + "' lacks a vertical coordinate system so the transformantion to -" + nameof(this.VerticalEpsg) + " " + this.VerticalEpsg + " is not well defined. Specify -" + nameof(this.InputVerticalEpsg) + " to indicate the vertical coordinate system of input files which do not define one. Common choices in the United States are EPSG " + Constant.Epsg.Navd88m + " (NAVD88 meters) and " + Constant.Epsg.Navd88ft + " (NAVD88 feet).");
                        }
                        else
                        {
                            // if cloud is missing a vertical CRS assume its vertical units are the same as those of -InputVerticalEpsg
                            cloudCrs = SpatialReferenceExtensions.CreateCompound(cloudCrs, inputVerticalCrs);
                        }
                    }

                    // transform cloud's bounding box, find new extents, new origin, and rotation between projected coordinate systems
                    // Transforming just the origin is not, in general, sufficient the x and y axes typically rotate between coordinate systems.
                    // As of 3.10, GDAL does not appear to perform validity checking and will transform out of bounds points without error (for
                    // example, a point with coordinates in feet which thus lies beyond the extent of a metric coordinate system). Experience with
                    // unusual transforms in QGIS and QT Modeler and QGIS code review suggests both are using Proj4 and that Proj4 silently performs
                    // inference to correct units mismatches between data and its assigned CRS. GDAL does not do this, hence the need for
                    // -InputScale.
                    CoordinateTransformation transform = new(cloudCrs, newCrs, new());
                    double currentBoundingBoxMinX = this.InputScale * cloud.Header.MinX;
                    double currentBoundingBoxMinY = this.InputScale * cloud.Header.MinY;
                    double currentBoundingBoxMinZ = this.InputScale * cloud.Header.MinZ;
                    double currentBoundingBoxMaxX = this.InputScale * cloud.Header.MaxX;
                    double currentBoundingBoxMaxY = this.InputScale * cloud.Header.MaxY;
                    double currentBoundingBoxMaxZ = this.InputScale * cloud.Header.MaxZ;
                    transform.TransformBounds(newBoundingBox, currentBoundingBoxMinX, currentBoundingBoxMinY, currentBoundingBoxMaxX, currentBoundingBoxMaxY, densify_pts: 21); // densify_pts = 21 per https://gdal.org/en/stable/doxygen/classOGRCoordinateTransformation.html

                    newXoriginMinMin[0] = this.InputScale * cloud.Header.XOffset;
                    newXoriginMinMin[1] = currentBoundingBoxMinX;
                    newXoriginMinMin[2] = currentBoundingBoxMinX;
                    newYoriginMinMax[0] = this.InputScale * cloud.Header.YOffset;
                    newYoriginMinMax[1] = currentBoundingBoxMinY;
                    newYoriginMinMax[2] = currentBoundingBoxMaxY;
                    newZoriginMinMax[0] = this.InputScale * cloud.Header.ZOffset;
                    newZoriginMinMax[1] = currentBoundingBoxMinZ;
                    newZoriginMinMax[2] = currentBoundingBoxMaxZ;
                    transform.TransformPoints(newXoriginMinMin.Length, newXoriginMinMin, newYoriginMinMax, newZoriginMinMax);

                    double newNorthAngleInRadians = 0.5 * Math.PI - Math.Atan2(newYoriginMinMax[2] - newYoriginMinMax[1], newXoriginMinMin[2] - newXoriginMinMin[1]);

                    Debug.Assert((newBoundingBox[0] < newBoundingBox[2]) && (newBoundingBox[1] < newBoundingBox[3]));
                    cloud.Header.MinX = newBoundingBox[0]; // min and max x and y are refreshed here for zero rotation
                    cloud.Header.MinY = newBoundingBox[1]; // values are updated based on actual rotation in second header write below
                    cloud.Header.MinZ = newZoriginMinMax[1];
                    cloud.Header.MaxX = newBoundingBox[2];
                    cloud.Header.MaxY = newBoundingBox[3];
                    cloud.Header.MaxZ = newZoriginMinMax[2];
                    cloud.Header.XOffset = newXoriginMinMin[0];
                    cloud.Header.YOffset = newYoriginMinMax[0];
                    cloud.Header.ZOffset = newZoriginMinMax[0];
                    cloud.SetSpatialReference(newCrs);

                    // update scale factors when linear units change
                    double horizontalScaleFactorChange = this.InputScale * cloudCrs.GetProjectedLinearUnitInM() / newCrs.GetProjectedLinearUnitInM();
                    if (horizontalScaleFactorChange != 1.0)
                    {
                        cloud.Header.XScaleFactor *= horizontalScaleFactorChange;
                        cloud.Header.YScaleFactor *= horizontalScaleFactorChange;
                    }

                    double verticalScaleFactorChange = this.InputScale * cloudCrs.GetVerticalLinearUnitInM() / newCrs.GetVerticalLinearUnitInM();
                    if (verticalScaleFactorChange != 1.0)
                    {
                        cloud.Header.ZScaleFactor *= verticalScaleFactorChange;
                    }

                    // write out copy of cloud with reprojected origin, scale, and coordinate system
                    string modifiedCloudPath = PathExtensions.AppendToFileName(cloudPath, " reprojected");
                    using LasWriter writer = LasWriter.CreateForPointWrite(modifiedCloudPath);
                    writer.WriteHeader(cloud);
                    writer.WriteVariableLengthRecordsAndUserData(cloud);
                    LasWriteTransformedResult writeResult = writer.WriteTransformedAndRepairedPoints(reader, cloud, -newNorthAngleInRadians, sourceID: null, repairClassification: false, repairReturnNumbers: false);
                    writer.WriteExtendedVariableLengthRecords(cloud);

                    Debug.Assert(writeResult.ReturnNumbersRepaired == 0);
                    if (newNorthAngleInRadians != 0.0)
                    {
                        cloud.Header.MaxX = writeResult.MaxX;
                        cloud.Header.MaxY = writeResult.MaxY;
                        cloud.Header.MinX = writeResult.MinX;
                        cloud.Header.MinY = writeResult.MinY;
                        writer.WriteHeader(cloud);
                    }

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
