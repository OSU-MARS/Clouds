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
        /// Create a <see cref="LasFile"/>  by reading the .las or .laz file's header and variable length records.
        /// </summary>
        public LasFile(LasReader reader, DateOnly? fallbackCreationDate)
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

            reader.ReadVariableAndExtendedVariableLengthRecords(this);
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

        public void SetOrigin(ReadOnlySpan<double> originXyz)
        {
            // translate point cloud to new origin
            double originX = originXyz[0];
            double originY = originXyz[1];
            double originZ = originXyz[2];

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

        public LasFile(LasReader reader, DateOnly? fallbackCreationDate)
            : base(reader, fallbackCreationDate)
        {
            this.Header = (THeader)base.Header;
        }
    }
}
