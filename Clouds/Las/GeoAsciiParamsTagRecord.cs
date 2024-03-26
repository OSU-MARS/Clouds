using System;
using System.Text;

namespace Mars.Clouds.Las
{
    public class GeoAsciiParamsTagRecord : VariableLengthRecordBase<UInt16>
    {
        public const UInt16 LasfProjectionRecordID = 34737;

        public string[] Values { get; private init; }

        public GeoAsciiParamsTagRecord(UInt16 reserved, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> stringBytes)
            : base(reserved, LasFile.LasfProjection, GeoAsciiParamsTagRecord.LasfProjectionRecordID, recordLengthAfterHeader, description)
        {
            this.Values = Encoding.UTF8.GetString(stringBytes).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
