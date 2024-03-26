using System;

namespace Mars.Clouds.Las
{
    public class OgcCoordinateSystemWktRecord : OgcWktRecord
    {
        public const UInt16 LasfProjectionRecordID = 2112;

        public OgcCoordinateSystemWktRecord(UInt16 reserved, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> wktBytes)
            : base(reserved, OgcCoordinateSystemWktRecord.LasfProjectionRecordID, recordLengthAfterHeader, description, wktBytes)
        {
        }
    }
}
