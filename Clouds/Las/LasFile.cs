using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.IO;

namespace Mars.Clouds.Las
{
    public class LasFile
    {
        public const string LasfProjection = "LASF_Projection";
        public const string LasfSpec = "LASF_Spec";
        public const byte MaxPointFormat = 10;
        public const string Signature = "LASF";

        public byte[] BytesAfterVariableLengthRecords { get; set; } // unstandardized padding bytes that can end up being requried
        public LasHeader10 Header { get; private init; }
        public List<VariableLengthRecord> VariableLengthRecords { get; private init; }
        public List<ExtendedVariableLengthRecord> ExtendedVariableLengthRecords { get; private init; }

        /// <summary>
        /// Create a <see cref="LasFile"/> by reading the .las or .laz file's header and variable length records.
        /// </summary>
        public LasFile(LasReader reader, bool discardOverrunningVlrs, DateOnly? fallbackCreationDate)
        {
            this.BytesAfterVariableLengthRecords = [];
            this.Header = reader.ReadHeader();
            this.VariableLengthRecords = [];
            this.ExtendedVariableLengthRecords = [];

            if (fallbackCreationDate != null)
            {
                this.Header.TryRepairFileCreationDate(fallbackCreationDate.Value);
            }
            this.Header.Validate();

            reader.ReadVariableAndExtendedVariableLengthRecords(this, discardOverrunningVlrs);
            if (this.Header.NumberOfVariableLengthRecords != this.VariableLengthRecords.Count)
            {
                throw new InvalidDataException(".las file header indicates " + this.Header.NumberOfVariableLengthRecords + " should be present but " + this.VariableLengthRecords.Count + " records were read.");
            }
        }

        /// <summary>
        /// Check offsets are consistent.
        /// </summary>
        /// <remarks>
        /// It's not uncommon .las files aren't tightly aligned, leaving extra bytes between the end of the variable length records and start
        /// of point data. This is maybe a feature in that it reduces the likelihood of needing to rewrite a .las file when variable length
        /// record (or version) changes are made.
        /// </remarks>
        //public void EnsureOffsetsSynchronized()
        //{
        //    UInt16 headerSizeInBytes = this.Header.HeaderSize;
        //    UInt32 offsetToPointData = headerSizeInBytes;
        //    for (int vlrIndex = 0; vlrIndex < this.VariableLengthRecords.Count; ++vlrIndex)
        //    {
        //        offsetToPointData += (UInt32)this.VariableLengthRecords[vlrIndex].GetSizeInBytes();
        //    }

        //    if (this.Header.OffsetToPointData != offsetToPointData)
        //    {
        //        this.Header.OffsetToPointData = offsetToPointData;
        //    }

        //    if (this.Header is LasHeader14 lasHeader14)
        //    {
        //        if (this.IsPointFormatCompressed())
        //        {
        //            throw new NotSupportedException("Don't know how to calculate offset to extended variable length records with .laz files.");
        //        }

        //        lasHeader14.StartOfFirstExtendedVariableLengthRecord = (UInt64)lasHeader14.OffsetToPointData + lasHeader14.NumberOfPointRecords * (UInt64)lasHeader14.PointDataRecordLength;
        //    }
        //}

        public int GetProjectedCoordinateSystemEpsg()
        {
            for (int vlrIndex = 0; vlrIndex < this.VariableLengthRecords.Count; ++vlrIndex)
            {
                VariableLengthRecord vlr = this.VariableLengthRecords[vlrIndex];
                if ((vlr.RecordID == GeoKeyDirectoryTagRecord.LasfProjectionRecordID) && String.Equals(vlr.UserID, LasFile.LasfProjection, StringComparison.Ordinal))
                {
                    GeoKeyDirectoryTagRecord geoKeyDirectory = (GeoKeyDirectoryTagRecord)vlr;
                    for (int keyIndex = 0; keyIndex < geoKeyDirectory.KeyEntries.Count; ++keyIndex)
                    {
                        GeoKeyEntry key = geoKeyDirectory.KeyEntries[keyIndex];
                        if (key.KeyID == GeoKey.ProjectedCSTypeGeoKey)
                        {
                            return key.ValueOrOffset;
                        }
                    }
                }
                // assume GeoAsciiParamsTagRecords can be ignored since they provide supporting information to GeoKeyDirectoryTagRecords
                // assume GeoDoubleParamsTagRecord can be ignored since they provide supporting information to GeoKeyDirectoryTagRecords
                else if (((vlr.RecordID == OgcCoordinateSystemWktRecord.LasfProjectionRecordID) || (vlr.RecordID == OgcMathTransformWktRecord.LasfProjectionRecordID)) && 
                         String.Equals(vlr.UserID, LasFile.LasfProjection, StringComparison.Ordinal))
                {
                    OgcWktRecord wktRecord = (OgcWktRecord)vlr;
                    return Int32.Parse(wktRecord.SpatialReference.GetAuthorityCode("PROJCS")); // wkt.SpatialReference.AutoIdentifyEPSG() tends to return 0
                }
            }

            throw new KeyNotFoundException("Could not find coordinate system record.");
        }

        public SpatialReference GetSpatialReference()
        {
            for (int vlrIndex = 0; vlrIndex < this.VariableLengthRecords.Count; ++vlrIndex)
            {
                VariableLengthRecord vlr = this.VariableLengthRecords[vlrIndex];
                if ((vlr.RecordID == GeoKeyDirectoryTagRecord.LasfProjectionRecordID) && String.Equals(vlr.UserID, LasFile.LasfProjection, StringComparison.Ordinal))
                {
                    GeoKeyDirectoryTagRecord crsRecord = (GeoKeyDirectoryTagRecord)vlr;
                    for (int keyIndex = 0; keyIndex < crsRecord.KeyEntries.Count; ++keyIndex)
                    {
                        GeoKeyEntry key = crsRecord.KeyEntries[keyIndex];
                        if (key.KeyID == GeoKey.ProjectedCSTypeGeoKey)
                        {
                            SpatialReference crs = new(null);
                            crs.ImportFromEpsg(key.ValueOrOffset);
                            return crs;
                        }
                    }
                }
                // assume GeoAsciiParamsTagRecords can be ignored since they provide supporting information to GeoKeyDirectoryTagRecords
                // assume GeoDoubleParamsTagRecord can be ignored since they provide supporting information to GeoKeyDirectoryTagRecords
                else if (((vlr.RecordID == OgcCoordinateSystemWktRecord.LasfProjectionRecordID) || (vlr.RecordID == OgcMathTransformWktRecord.LasfProjectionRecordID)) && 
                         String.Equals(vlr.UserID, LasFile.LasfProjection, StringComparison.Ordinal))
                {
                    OgcWktRecord wktRecord = (OgcWktRecord)vlr;
                    return wktRecord.SpatialReference;
                }
            }

            throw new KeyNotFoundException("Could not find coordinate system record.");
        }

        public bool IsPointFormatCompressed()
        {
            return (this.Header.PointDataRecordFormat & LazVariableLengthRecord.PointDataFormatMask) == LazVariableLengthRecord.PointDataFormatMask;
        }

        public void RotateExtents(double rotationXYinDegrees)
        {
            double rotationXYinRadians = Double.Pi / 180.0 * rotationXYinDegrees;
            double sinRotationXY = Double.Sin(rotationXYinRadians);
            double cosRotationXY = Double.Cos(rotationXYinRadians);

            // rotated x extents
            double maxX = this.Header.MaxX;
            double minX = this.Header.MinX;
            double maxY = this.Header.MaxY;
            double minY = this.Header.MinY;
            double minXminYcornerRotatedX = minX * cosRotationXY - minY * sinRotationXY;
            double minXrotated = minXminYcornerRotatedX;
            double maxXrotated = minXminYcornerRotatedX;

            double minXmaxYcornerRotatedX = minX * cosRotationXY - maxY * sinRotationXY;
            if (minXmaxYcornerRotatedX < minXrotated)
            {
                minXrotated = minXmaxYcornerRotatedX;
            }
            if (minXmaxYcornerRotatedX > maxXrotated)
            {
                maxXrotated = minXmaxYcornerRotatedX;
            }

            double maxXminYcornerRotatedX = maxX * cosRotationXY - minY * sinRotationXY;
            if (maxXminYcornerRotatedX < minXrotated)
            {
                minXrotated = maxXminYcornerRotatedX;
            }
            if (maxXminYcornerRotatedX > maxXrotated)
            {
                maxXrotated = maxXminYcornerRotatedX;
            }

            double maxXmaxYcornerRotatedX = maxX * cosRotationXY - maxY * sinRotationXY;
            if (maxXmaxYcornerRotatedX < minXrotated)
            {
                minXrotated = maxXmaxYcornerRotatedX;
            }
            if (maxXmaxYcornerRotatedX > maxXrotated)
            {
                maxXrotated = maxXmaxYcornerRotatedX;
            }

            // rotated y extents
            double minXminYcornerRotatedY = minX * sinRotationXY + minY * cosRotationXY;
            double minYrotated = minXminYcornerRotatedY;
            double maxYrotated = minXminYcornerRotatedY;

            double minXmaxYcornerRotatedY = minX * sinRotationXY + maxY * cosRotationXY;
            if (minXmaxYcornerRotatedY < minYrotated)
            {
                minYrotated = minXmaxYcornerRotatedY;
            }
            if (minXmaxYcornerRotatedY > maxYrotated)
            {
                maxYrotated = minXmaxYcornerRotatedY;
            }

            double maxXminYcornerRotatedY = maxX * sinRotationXY + minY * cosRotationXY;
            if (maxXminYcornerRotatedY < minYrotated)
            {
                minYrotated = maxXminYcornerRotatedY;
            }
            if (maxXminYcornerRotatedY > maxYrotated)
            {
                maxYrotated = maxXminYcornerRotatedY;
            }

            double maxXmaxYcornerRotatedY = maxX * sinRotationXY + maxY * cosRotationXY;
            if (maxXmaxYcornerRotatedY < minYrotated)
            {
                minYrotated = maxXmaxYcornerRotatedY;
            }
            if (maxXmaxYcornerRotatedY > maxYrotated)
            {
                maxYrotated = maxXmaxYcornerRotatedY;
            }

            // update extents
            this.Header.MaxX = maxXrotated;
            this.Header.MinX = minXrotated;
            this.Header.MaxY = maxYrotated;
            this.Header.MinY = minYrotated;
        }

        public void SetOrigin(double originX, double originY, double originZ)
        {
            // if needed, the origin's precision can be matched to the cloud's scale precision
            // LAStools wants origin and extent doubles are truncated to match the scale but this is not required by the LAS 1.4 R15
            // specification (§2.4). Other software emits full precision origins.

            // translate point cloud to new origin
            double xShift = originX - this.Header.XOffset;
            this.Header.MinX += xShift;
            this.Header.MaxX += xShift;
            double yShift = originY - this.Header.YOffset;
            this.Header.MinY += yShift;
            this.Header.MaxY += yShift;
            double zShift = originZ - this.Header.ZOffset;
            this.Header.MinZ += zShift;
            this.Header.MaxZ += zShift;
            this.Header.XOffset = originX;
            this.Header.YOffset = originY;
            this.Header.ZOffset = originZ;
        }

        public void SetSpatialReference(SpatialReference crs)
        {
            bool wktRecordUpdated = false;
            for (int vlrIndex = 0; vlrIndex < this.VariableLengthRecords.Count; ++vlrIndex)
            {
                VariableLengthRecord vlr = this.VariableLengthRecords[vlrIndex];
                if ((vlr.RecordID == GeoKeyDirectoryTagRecord.LasfProjectionRecordID) && String.Equals(vlr.UserID, LasFile.LasfProjection, StringComparison.Ordinal))
                {
                    this.VariableLengthRecords.RemoveAt(vlrIndex--);
                    this.Header.NumberOfVariableLengthRecords -= 1;
                    this.Header.OffsetToPointData -= (UInt32)(VariableLengthRecord.HeaderSizeInBytes + ((GeoKeyDirectoryTagRecord)vlr).RecordLengthAfterHeader);
                }
                else if (((vlr.RecordID == OgcCoordinateSystemWktRecord.LasfProjectionRecordID) || (vlr.RecordID == OgcMathTransformWktRecord.LasfProjectionRecordID)) &&
                         String.Equals(vlr.UserID, LasFile.LasfProjection, StringComparison.Ordinal))
                {
                    OgcWktRecord wktRecord = (OgcWktRecord)vlr;
                    UInt16 initialRecordLengthAfterHeader = wktRecord.RecordLengthAfterHeader;
                    wktRecord.SetSpatialReference(crs);
                    UInt16 updatedRecordLengthAfterHeader = wktRecord.RecordLengthAfterHeader;
                    this.Header.OffsetToPointData += (UInt32)(updatedRecordLengthAfterHeader - initialRecordLengthAfterHeader);
                    wktRecordUpdated = true;
                }
            }

            if (wktRecordUpdated == false)
            {
                OgcCoordinateSystemWktRecord wktVlr = OgcCoordinateSystemWktRecord.Create(crs);
                this.VariableLengthRecords.Add(wktVlr);
                this.Header.NumberOfVariableLengthRecords += 1;
                this.Header.OffsetToPointData += (UInt32)(VariableLengthRecord.HeaderSizeInBytes + wktVlr.RecordLengthAfterHeader);
            }
        }
    }

    public class LasFile<THeader> : LasFile where THeader : LasHeader10
    {
        public new THeader Header { get; private init; }

        public LasFile(LasReader reader, bool discardOverrunningVlrs, DateOnly? fallbackCreationDate)
            : base(reader, discardOverrunningVlrs, fallbackCreationDate)
        {
            this.Header = (THeader)base.Header;
        }
    }
}
