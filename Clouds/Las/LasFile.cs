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

        public LasHeader10 Header { get; private init; }
        public List<VariableLengthRecord> VariableLengthRecords { get; private init; }
        public List<ExtendedVariableLengthRecord> ExtendedVariableLengthRecords { get; private init; }

        /// <summary>
        /// Create a <see cref="LasFile"/>  by reading the .las or .laz file's header and variable length records.
        /// </summary>
        public LasFile(LasReader reader, DateOnly? fallbackCreationDate)
        {
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
                            crs.ImportFromEPSG(key.ValueOrOffset);
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
