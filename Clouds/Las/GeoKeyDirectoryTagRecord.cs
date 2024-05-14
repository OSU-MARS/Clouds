using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Mars.Clouds.Las
{
    public class GeoKeyDirectoryTagRecord : VariableLengthRecord
    {
        public const int CoreSizeInBytes = 8;
        public const UInt16 LasfProjectionRecordID = 34735;

        public UInt16 KeyDirectoryVersion { get; set; }
        public UInt16 KeyRevision { get; set; }
        public UInt16 MinorRevision { get; set; }
        public UInt16 NumberOfKeys { get; set; }
        public List<GeoKeyEntry> KeyEntries { get; set; }

        public GeoKeyDirectoryTagRecord(UInt16 reserved, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> vlrBytes) 
            : base(reserved, LasFile.LasfProjection, GeoKeyDirectoryTagRecord.LasfProjectionRecordID, recordLengthAfterHeader, description)
        {
            this.KeyEntries = [];

            this.KeyDirectoryVersion = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes);
            this.KeyRevision = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[2..]);
            this.MinorRevision = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[4..]);
            this.NumberOfKeys = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[6..]);
        }

        public override void Write(Stream stream)
        {
            this.WriteHeader(stream);

            Span<byte> vlrBytes = stackalloc byte[this.RecordLengthAfterHeader];
            BinaryPrimitives.WriteUInt16LittleEndian(vlrBytes, this.KeyDirectoryVersion);
            BinaryPrimitives.WriteUInt16LittleEndian(vlrBytes[2..], this.KeyRevision);
            BinaryPrimitives.WriteUInt16LittleEndian(vlrBytes[4..], this.MinorRevision);
            BinaryPrimitives.WriteUInt16LittleEndian(vlrBytes[6..], this.NumberOfKeys);

            for (int keyIndex = 0, keyOffset = 8; keyIndex < this.KeyEntries.Count; ++keyIndex, keyOffset += 8)
            {
                GeoKeyEntry entry = this.KeyEntries[keyIndex];
                BinaryPrimitives.WriteDoubleBigEndian(vlrBytes[keyOffset..], entry.KeyID);
                BinaryPrimitives.WriteDoubleBigEndian(vlrBytes[(keyOffset + 2)..], entry.TiffTagLocation);
                BinaryPrimitives.WriteDoubleBigEndian(vlrBytes[(keyOffset + 4)..], entry.Count);
                BinaryPrimitives.WriteDoubleBigEndian(vlrBytes[(keyOffset + 6)..], entry.ValueOrOffset);
            }

            stream.Write(vlrBytes);
        }
    }
}
