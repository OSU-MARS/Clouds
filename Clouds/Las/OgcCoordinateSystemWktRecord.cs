using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;
using System.Text;

namespace Mars.Clouds.Las
{
    public class OgcCoordinateSystemWktRecord : OgcWktRecord
    {
        public const UInt16 LasfProjectionRecordID = 2112;

        protected OgcCoordinateSystemWktRecord(UInt16 recordLengthAfterHeader, string description, SpatialReference crs)
            : base(Constant.Las.VlrReservedHeaderValue, OgcCoordinateSystemWktRecord.LasfProjectionRecordID, recordLengthAfterHeader, description, crs)
        {
            this.SpatialReference = crs;
        }

        public OgcCoordinateSystemWktRecord(UInt16 reserved, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> wktBytes)
            : base(reserved, OgcCoordinateSystemWktRecord.LasfProjectionRecordID, recordLengthAfterHeader, description, wktBytes)
        {
        }

        public static OgcCoordinateSystemWktRecord Create(SpatialReference crs)
        {
            string wkt = crs.GetWkt();
            byte[] wktBytes = Encoding.UTF8.GetBytes(wkt);
            return new OgcCoordinateSystemWktRecord((UInt16)wktBytes.Length, "WKT Projection", crs);
        }
    }
}
