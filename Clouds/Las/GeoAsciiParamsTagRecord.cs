using System;
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

        public override int GetSizeInBytes()
        {
            int sizeInBytes = VariableLengthRecord.HeaderSizeInBytes;
            for (int valueIndex = 0; valueIndex < this.Values.Length; ++valueIndex)
            {
                byte[] valueBytes = Encoding.UTF8.GetBytes(this.Values[valueIndex]);
                sizeInBytes += valueBytes.Length + 1; // trailing null
            }

            return sizeInBytes;
        }

        public override void Write(Stream stream)
        {
            this.WriteHeader(stream);

            // write all but the last value
            // The LAS specification states values are separated by null characters but not that the last value is null terminated.
            byte[] valueBytes;
            int valueBytesWritten = 0;
            for (int valueIndex = 0; valueIndex < this.Values.Length - 1; ++valueIndex)
            {
                valueBytes = Encoding.UTF8.GetBytes(this.Values[valueIndex]);
                stream.Write(valueBytes);

                stream.WriteByte(0);
                valueBytesWritten += valueBytes.Length + 1;
            }

            // write the last value
            valueBytes = Encoding.UTF8.GetBytes(this.Values[^1]);
            stream.Write(valueBytes);
            valueBytesWritten += valueBytes.Length;

            if (valueBytesWritten != this.RecordLengthAfterHeader)
            {
                throw new InvalidOperationException($"GeoAsciiParamsTag record values totalled {valueBytesWritten} bytes (including null separators between values) which does not match the record length after header of {this.RecordLengthAfterHeader} bytes.");
            }
        }
    }
}
