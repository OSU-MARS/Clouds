using System;

namespace Mars.Clouds.Las
{
    public class OgcMathTransformWktRecord : OgcWktRecord
    {
        public const UInt16 LasfProjectionRecordID = 2111;

        public OgcMathTransformWktRecord(UInt16 reserved, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> wktBytes)
            : base(reserved, OgcMathTransformWktRecord.LasfProjectionRecordID, recordLengthAfterHeader, description, wktBytes)
        {
        }
    }
}
