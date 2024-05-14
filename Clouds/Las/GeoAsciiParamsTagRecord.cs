using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Mars.Clouds.Las
{
    public class GeoAsciiParamsTagRecord : VariableLengthRecord
    {
        public const UInt16 LasfProjectionRecordID = 34737;

        public string[] Values { get; private init; }

        public GeoAsciiParamsTagRecord(UInt16 reserved, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> stringBytes)
            : base(reserved, LasFile.LasfProjection, GeoAsciiParamsTagRecord.LasfProjectionRecordID, recordLengthAfterHeader, description)
        {
            this.Values = Encoding.UTF8.GetString(stringBytes).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }

        public override void Write(Stream stream)
        {
            this.WriteHeader(stream);

            int valueBytesWritten = 0;
            for (int valueIndex = 0; valueIndex < this.Values.Length; ++valueIndex)
            {
                byte[] valueBytes = Encoding.UTF8.GetBytes(this.Values[valueIndex]);
                stream.Write(valueBytes);

                valueBytesWritten += valueBytes.Length;
                if (valueBytesWritten < this.RecordLengthAfterHeader)
                {
                    stream.WriteByte(0);
                    ++valueBytesWritten;
                }
            }

            Debug.Assert(valueBytesWritten == this.RecordLengthAfterHeader);
        }
    }
}
