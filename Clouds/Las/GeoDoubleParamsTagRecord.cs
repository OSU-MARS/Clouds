using System;
using System.Buffers.Binary;

namespace Mars.Clouds.Las
{
    public class GeoDoubleParamsTagRecord : VariableLengthRecordBase<UInt16>
    {
        public const UInt16 LasfProjectionRecordID = 34736;

        public double[] Values { get; private init; }

        public GeoDoubleParamsTagRecord(UInt16 reserved, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> doubleBytes)
            : base(reserved, LasFile.LasfProjection, GeoDoubleParamsTagRecord.LasfProjectionRecordID, recordLengthAfterHeader, description)
        {
            if (doubleBytes.Length % sizeof(double) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(doubleBytes), doubleBytes.Length + " bytes of VLR data is not an integer number of " + sizeof(double) + " byte double precision values.");
            }

            int doubles = doubleBytes.Length / sizeof(double);
            this.Values = new double[doubles];
            for (int index = 0; index < doubles; ++index)
            {
                this.Values[index] = BinaryPrimitives.ReadDoubleLittleEndian(doubleBytes[(sizeof(double) * index)..]);
            }
        }
    }
}
