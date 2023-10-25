using System;
using System.Buffers.Binary;

namespace Mars.Clouds.Las
{
    public class GeoKeyEntry
    {
        public const int SizeInBytes = 8;

        public UInt16 KeyID { get; set; }
        public UInt16 TiffTagLocation { get; set; }
        public UInt16 Count { get; set; }
        public UInt16 ValueOrOffset { get; set; }

        public GeoKeyEntry(ReadOnlySpan<byte> vlrBytes) 
        {
            this.KeyID = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes);
            this.TiffTagLocation = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[2..]);
            this.Count = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[4..]);
            this.ValueOrOffset = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[6..]);
        }
    }
}
