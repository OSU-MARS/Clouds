using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Mars.Clouds.Las
{
    public class GeoKeyDirectoryTagRecord : VariableLengthRecordBase<UInt16>
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
    }
}
