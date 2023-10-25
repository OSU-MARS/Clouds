using OSGeo.OSR;
using System;
using System.Text;

namespace Mars.Clouds.Las
{
    public class OgcCoordinateSystemWktRecord : VariableLengthRecordBase<UInt16>
    {
        public const UInt16 LasfProjectionRecordID = 2112;

        public SpatialReference SpatialReference { get; set; }

        public OgcCoordinateSystemWktRecord(UInt16 reserved, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> wktBytes)
            : base(reserved, LasFile.LasfProjection, OgcCoordinateSystemWktRecord.LasfProjectionRecordID, recordLengthAfterHeader, description)
        {
            this.SpatialReference = new(Encoding.UTF8.GetString(wktBytes));
        }
    }
}
