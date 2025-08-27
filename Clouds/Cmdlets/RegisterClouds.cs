﻿using Mars.Clouds.Cmdlets.Hardware;
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
    [Cmdlet(VerbsLifecycle.Register, "Clouds")]
    public class RegisterClouds : GdalCmdlet
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

        [Parameter(Mandatory = true, HelpMessage = "Elevation of point cloud's origin in the units of the coordinate system specified by -VerticalEpsg.")]
        [ValidateRange(-420.0, 8848.0)]
        public double Z { get; set; }

        [Parameter(HelpMessage = "EPSG of projected coordinate system to assign to point cloud. Default is 32610 (WGS 84 / UTM zone 10N).")]
        [ValidateRange(Constant.Epsg.Min, Constant.Epsg.Max)]
        public int HorizontalEpsg { get; set; }

        [Parameter(HelpMessage = "EPSG of vertical coordinate system to assign to point cloud. Default is 5703 (NAVD88 meters), NAVD88 feet is 8228.")]
        [ValidateRange(Constant.Epsg.Min, Constant.Epsg.Max)]
        public int VerticalEpsg { get; set; }

        [Parameter(HelpMessage = "Initial source ID to assign. Incremented for each file found thereafter.")]
        [ValidateRange(0, UInt16.MaxValue)]
        public int SourceID { get; set; }

        [Parameter(HelpMessage = "Rotation, in degrees, to apply in the xy plane. Rotation is in the cartesian coordinate sense of from +x to +y and is therefore in the opposite direction from compass azimuth (use a negative -RotationXY to rotate clockwise).")]
        [ValidateRange(-360.0F, 360.0F)]
        public double[] RotationXY { get; set; }

        [Parameter(HelpMessage = "Translation to apply in the x direction relative to the specified latitude and longitude. Units are in the coordinate system specified by -HorizontalEpsg.")]
        [ValidateNotNullOrEmpty]
        [ValidateRange(-100.0F, 100.0F)]
        public double[] NudgeX { get; set; }

        [Parameter(HelpMessage = "Translation to apply in the y direction relative to the specified latitude and longitude. Units are in the coordinate system specified by -HorizontalEpsg.")]
        [ValidateNotNullOrEmpty]
        [ValidateRange(-100.0F, 100.0F)]
        public double[] NudgeY { get; set; }

        [Parameter(HelpMessage = "Fallback date to use if header is missing year or day of year information.")]
        public DateOnly? FallbackDate { get; set; }

        [Parameter(HelpMessage = "Set x, y, and z ranges in header to actual point extents (Faro Connect oversizes x and y, possibly FJ Dynamics).")]
        public SwitchParameter RepairBounds { get; set; }

        [Parameter(HelpMessage = "Change never classified points to unclassified and unclassified points to ground (FJ Dynamics Trion Model).")]
        public SwitchParameter RepairClassification { get; set; }

        [Parameter(HelpMessage = "Set points with return number zero and zero total returns (Faro Connect, FJ Dynamics P1 and S1) to single returns and return number 1.")]
        public SwitchParameter RepairReturn { get; set; }

        [Parameter(HelpMessage = "Units of the input point cloud. Meters ('m') or feet ('ft'). Default is meters.")]
        [ValidateSet(Constant.Crs.LinearUnitMeter, Constant.Crs.LinearUnitFoot)]
        public string ScannerUnit { get; set; }

        public RegisterClouds()
        {
            this.Las = [];
            this.Lat = Double.NaN;
            this.Long = Double.NaN;
            this.Z = Double.NaN;
            this.HorizontalEpsg = Constant.Epsg.Utm10N;
            this.VerticalEpsg = Constant.Epsg.Navd88m;
            this.SourceID = 1;
            this.RotationXY = [ 0.0 ];
            this.NudgeX = [ 0.0 ];
            this.NudgeY = [0.0];
            this.FallbackDate = null;
            this.RepairClassification = false;
            this.RepairReturn = false;
            this.ScannerUnit = Constant.Crs.LinearUnitMeter;
        }

        protected override void ProcessRecord()
        {
            // check inputs
            List<string> cloudPaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);
            if (cloudPaths.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.Las), "-" + nameof(this.Las) + " = '" + String.Join(", ", this.Las) + "' does not match any existing point clouds.");
            }
            if ((this.RotationXY.Length != 1) && (this.RotationXY.Length != cloudPaths.Count))
            {
                throw new ParameterOutOfRangeException(nameof(this.RotationXY), "-" + nameof(this.RotationXY) + " must have either single value or as many values as there are clouds to register. There are " + this.RotationXY.Length + " values in -" + nameof(this.RotationXY) + " and " + cloudPaths.Count + " clouds.");
            }
            if ((this.NudgeX.Length != 1) && (this.NudgeX.Length != cloudPaths.Count))
            {
                throw new ParameterOutOfRangeException(nameof(this.NudgeX), "-" + nameof(this.NudgeX) + " must have either single value or as many values as there are clouds to register. There are " + this.NudgeX.Length + " values in -" + nameof(this.NudgeX) + " and " + cloudPaths.Count + " clouds.");
            }
            if ((this.NudgeY.Length != 1) && (this.NudgeY.Length != cloudPaths.Count))
            {
                throw new ParameterOutOfRangeException(nameof(this.NudgeY), "-" + nameof(this.NudgeY) + " must have either single value or as many values as there are clouds to register. There are " + this.NudgeY.Length + " values in -" + nameof(this.NudgeY) + " and " + cloudPaths.Count + " clouds.");
            }

            // reproject point cloud's horizontal origin from WGS84 to cloud's coordinate system
            // Vertical origin from -Z is passed through transformation since no vertical CRS is attached to the WGS84 CRS. This shouldn't be necessary but GDAL
            // lacks a Geometry.AddPoint(double x, double y) overload.
            SpatialReference wgs84 = SpatialReferenceExtensions.Create(Constant.Epsg.Wgs84);
            SpatialReference cloudCrs = SpatialReferenceExtensions.CreateCompound(this.HorizontalEpsg, this.VerticalEpsg);

            CoordinateTransformation transform = new(wgs84, cloudCrs, new());
            Geometry origin = new(wkbGeometryType.wkbPoint25D);
            origin.AddPoint(this.Lat, this.Long, this.Z); // GDAL reverses x and y for WGS84
            if (origin.Transform(transform) != 0)
            {
                throw new ParameterOutOfRangeException(nameof(this.HorizontalEpsg), "Could not transform point cloud origin " + this.Lat + ", " + this.Long + ", " + this.Z + " to EPSG:" + this.HorizontalEpsg + ".");
            }
            double[] commonOriginXyz = new double[3]; // can't use stackalloc as GDAL 3.8.3 C# bindings don't support Span<T>
            origin.GetPoint(0, commonOriginXyz);
            double scanAnchorX = commonOriginXyz[0];
            double scanAnchorY = commonOriginXyz[1];
            double scanAnchorZ = commonOriginXyz[2];

            // point cloud scale requires adjustement if the scanner and destination CRSes use different units
            double scannerLinearUnitInM = SpatialReferenceExtensions.GetLinearUnitInM(this.ScannerUnit);
            double horizontalScaleFactorChange = scannerLinearUnitInM / cloudCrs.GetProjectedLinearUnitInM();
            double verticalScaleFactorChange = scannerLinearUnitInM / cloudCrs.GetVerticalLinearUnitInM();

            // set point clouds' origins, coordinate systems, and source IDs
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasWriter.GetPointCopyEditBandwidth();
            HardwareCapabilities hardwareCapabilities = HardwareCapabilities.Current;
            int readThreads = Int32.Min(hardwareCapabilities.GetPracticalReadThreadCount(this.Las, this.SessionState.Path.CurrentLocation.Path, driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs), this.DataThreads);

            int cloudRegistrationsInitiated = -1;
            int cloudRegistrationsCompleted = 0;
            ParallelTasks cloudRegistrationTasks = new(Int32.Min(readThreads, cloudPaths.Count), () =>
            {
                for (int cloudIndex = Interlocked.Increment(ref cloudRegistrationsInitiated); cloudIndex < cloudPaths.Count; cloudIndex = Interlocked.Increment(ref cloudRegistrationsInitiated))
                {
                    string cloudPath = cloudPaths[cloudIndex];
                    using LasReader reader = LasReader.CreateForPointRead(cloudPath);
                    LasFile cloud = new(reader, this.FallbackDate); // leaves reader positioned at start of points

                    // update cloud extents
                    double rotationXYinDegrees = this.RotationXY.Length == 1 ? this.RotationXY[0] : this.RotationXY[cloudIndex];
                    double rotationXYinRadians = Double.Pi / 180.0 * rotationXYinDegrees;
                    if (rotationXYinRadians != 0.0)
                    {
                        cloud.RotateExtents(rotationXYinRadians); // update extents to match rotation of points below
                    }

                    // translate cloud
                    double nudgeXinCrsUnits = this.NudgeX.Length == 1 ? this.NudgeX[0] : this.NudgeX[cloudIndex];
                    double nudgeYinCrsUnits = this.NudgeY.Length == 1 ? this.NudgeY[0] : this.NudgeY[cloudIndex];
                    double originX = scanAnchorX + nudgeXinCrsUnits;
                    double originY = scanAnchorY + nudgeYinCrsUnits;
                    cloud.SetOriginAndUpdateExtents(originX, originY, scanAnchorZ);

                    // update scale factors when linear units change
                    if (horizontalScaleFactorChange != 1.0)
                    {
                        cloud.Header.XScaleFactor *= horizontalScaleFactorChange;
                        cloud.Header.YScaleFactor *= horizontalScaleFactorChange;
                    }
                    if (verticalScaleFactorChange != 1.0)
                    {
                        cloud.Header.ZScaleFactor *= verticalScaleFactorChange;
                    }

                    // assign cloud coordinate system
                    cloud.SetSpatialReference(cloudCrs);

                    // write registered version of cloud
                    string modifiedCloudPath = PathExtensions.AppendToFileName(cloudPath, " registered");
                    using LasWriter writer = LasWriter.CreateForPointWrite(modifiedCloudPath);
                    writer.WriteHeader(cloud);
                    writer.WriteVariableLengthRecordsAndUserData(cloud);

                    LinearCoordinateTransform scaledTransformInSourceCrsUnits = new()
                    {
                        RotationXYinRadians = rotationXYinRadians
                    };
                    LasWriteTransformedResult writeResult = writer.WriteTransformedAndRepairedPoints(reader, cloud, scaledTransformInSourceCrsUnits, (UInt16)(this.SourceID + cloudIndex), this.RepairBounds, this.RepairClassification, this.RepairReturn);
                    writer.WriteExtendedVariableLengthRecords(cloud);

                    bool updateHeader = false;
                    if (this.RepairBounds)
                    {
                        if ((Double.IsNaN(writeResult.MinX) == false) && (cloud.Header.MinX != writeResult.MinX))
                        {
                            cloud.Header.MinX = writeResult.MinX;
                            updateHeader = true;
                        }
                        if ((Double.IsNaN(writeResult.MaxX) == false) && (cloud.Header.MaxX != writeResult.MaxX))
                        {
                            cloud.Header.MaxX = writeResult.MaxX;
                            updateHeader = true;
                        }
                        if ((Double.IsNaN(writeResult.MinY) == false) && (cloud.Header.MinY != writeResult.MinY))
                        {
                            cloud.Header.MinY = writeResult.MinY;
                            updateHeader = true;
                        }
                        if ((Double.IsNaN(writeResult.MaxY) == false) && (cloud.Header.MaxY != writeResult.MaxY))
                        {
                            cloud.Header.MaxY = writeResult.MaxY;
                            updateHeader = true;
                        }
                        if ((Double.IsNaN(writeResult.MinZ) == false) && (cloud.Header.MinZ != writeResult.MinZ))
                        {
                            cloud.Header.MinZ = writeResult.MinZ;
                            updateHeader = true;
                        }
                        if ((Double.IsNaN(writeResult.MaxZ) == false) && (cloud.Header.MaxZ != writeResult.MaxZ))
                        {
                            cloud.Header.MaxZ = writeResult.MaxZ;
                            updateHeader = true;
                        }
                    }

                    if (writeResult.ReturnNumbersRepaired > 0)
                    {
                        // debatable: should synthetic return numbers flag be set for LAS 1.3+ files if return numbers are, in fact, repaired?
                        // cloud.Header13.GlobalEncoding |= GlobalEncoding.SyntheticReturnNumbers
                        cloud.Header.IncrementFirstReturnCount(writeResult.ReturnNumbersRepaired);
                        updateHeader = true;
                    }

                    if (updateHeader)
                    {
                        writer.WriteHeader(cloud);
                    }

                    Interlocked.Increment(ref cloudRegistrationsCompleted);
                    if (this.Stopping)
                    {
                        break;
                    }
                }
            }, this.CancellationTokenSource);

            TimedProgressRecord progress = new("Register-Clouds", "Registered " + cloudRegistrationsCompleted + " of " + cloudPaths.Count + " point clouds...");
            while (cloudRegistrationTasks.WaitAll(Constant.DefaultProgressInterval) == false) 
            {
                progress.StatusDescription = "Registered " + cloudRegistrationsCompleted + " of " + cloudPaths.Count + " point clouds...";
                progress.Update(cloudRegistrationsCompleted, cloudPaths.Count);
                this.WriteProgress(progress);
            }

            base.ProcessRecord();
        }
    }
}
