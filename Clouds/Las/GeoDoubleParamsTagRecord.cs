using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;

namespace Mars.Clouds.Las
{
    public class GeoDoubleParamsTagRecord : VariableLengthRecord
    {
        public const UInt16 LasfProjectionRecordID = 34736;

        public double[] Values { get; private init; }

        public GeoDoubleParamsTagRecord(UInt16 reserved, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> doubleBytes)
            : base(reserved, LasFile.LasfProjection, GeoDoubleParamsTagRecord.LasfProjectionRecordID, recordLengthAfterHeader, description)
        {
            if (doubleBytes.Length % sizeof(double) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(doubleBytes), $"{doubleBytes.Length} bytes of VLR data is not an integer number of {sizeof(double)} byte double precision values.");
            }

            int doubles = doubleBytes.Length / sizeof(double);
            this.Values = new double[doubles];
            for (int valueIndex = 0; valueIndex < doubles; ++valueIndex)
            {
                this.Values[valueIndex] = BinaryPrimitives.ReadDoubleLittleEndian(doubleBytes[(sizeof(double) * valueIndex)..]);
            }
        }

        public override int GetSizeInBytes()
        {
            return VariableLengthRecord.HeaderSizeInBytes + sizeof(double) * this.Values.Length;
        }

        public override void Write(Stream stream)
        {
            Debug.Assert(this.RecordLengthAfterHeader == sizeof(double) * this.Values.Length);
            this.WriteHeader(stream);

            Span<byte> valueBytes = stackalloc byte[sizeof(double) * this.Values.Length];
            for (int valueIndex = 0; valueIndex < this.Values.Length; ++valueIndex)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(valueBytes[(sizeof(double) * valueIndex)..], this.Values[valueIndex]);
            }

            stream.Write(valueBytes);
        }
    }
}
