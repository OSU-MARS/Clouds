using Mars.Clouds.Laz;
using OSGeo.OSR;
using System;
using System.Collections.Generic;

namespace Mars.Clouds.Las
{
    public class LasFile
    {
        public const string LasfProjection = "LASF_Projection";
        public const string LasfSpec = "LASF_Spec";
        public const byte MaxPointFormat = 10;
        public const string Signature = "LASF";

        public LasHeader10 Header { get; private init; }
        public List<VariableLengthRecordBase> VariableLengthRecords { get; private init; }

        /// <summary>
        /// Create a <see cref="LasFile"/>  by reading the .las or .laz file's header and variable length records.
        /// </summary>
        public LasFile(LasReader reader)
        {
            this.Header = reader.ReadHeader();
            this.VariableLengthRecords = new();

            reader.ReadVariableLengthRecords(this);
        }

        public int GetProjectedCoordinateSystemEpsg()
        {
            for (int vlrIndex = 0; vlrIndex < this.VariableLengthRecords.Count; ++vlrIndex)
            {
                VariableLengthRecordBase vlr = this.VariableLengthRecords[vlrIndex];
                if ((vlr.RecordID == GeoKeyDirectoryTagRecord.LasfProjectionRecordID) && String.Equals(vlr.UserID, LasFile.LasfProjection, StringComparison.Ordinal))
                {
                    GeoKeyDirectoryTagRecord crsRecord = (GeoKeyDirectoryTagRecord)vlr;
                    for (int keyIndex = 0; keyIndex < crsRecord.KeyEntries.Count; ++keyIndex)
                    {
                        GeoKeyEntry key = crsRecord.KeyEntries[keyIndex];
                        if (key.KeyID == GeoKey.ProjectedCSTypeGeoKey)
                        {
                            return key.ValueOrOffset;
                        }
                    }
                }
                else if ((vlr.RecordID == OgcCoordinateSystemWktRecord.LasfProjectionRecordID) && String.Equals(vlr.UserID, LasFile.LasfProjection, StringComparison.Ordinal))
                {
                    OgcCoordinateSystemWktRecord wktRecord = (OgcCoordinateSystemWktRecord)vlr;
                    return Int32.Parse(wktRecord.SpatialReference.GetAuthorityCode("PROJCS")); // wkt.SpatialReference.AutoIdentifyEPSG() tends to return 0
                }
            }

            throw new KeyNotFoundException("Could not find coordinate system record.");
        }

        public SpatialReference GetSpatialReference()
        {
            for (int vlrIndex = 0; vlrIndex < this.VariableLengthRecords.Count; ++vlrIndex)
            {
                VariableLengthRecordBase vlr = this.VariableLengthRecords[vlrIndex];
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
                else if ((vlr.RecordID == OgcCoordinateSystemWktRecord.LasfProjectionRecordID) && String.Equals(vlr.UserID, LasFile.LasfProjection, StringComparison.Ordinal))
                {
                    OgcCoordinateSystemWktRecord wktRecord = (OgcCoordinateSystemWktRecord)vlr;
                    return wktRecord.SpatialReference;
                }
            }

            throw new KeyNotFoundException("Could not find coordinate system record.");
        }

        public bool IsPointFormatCompressed()
        {
            return (this.Header.PointDataRecordFormat & LazVariableLengthRecord.PointDataFormatMask) == LazVariableLengthRecord.PointDataFormatMask;
        }
    }

    public class LasFile<THeader> : LasFile where THeader : LasHeader10
    {
        public new THeader Header { get; private init; }

        public LasFile(LasReader reader)
            : base(reader)
        {
            this.Header = (THeader)base.Header;
        }
    }
}
