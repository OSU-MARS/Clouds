using System;
using System.Buffers.Binary;
using System.IO;
using Mars.Clouds.Laz;

namespace Mars.Clouds.Las
{
    public class LazVariableLengthRecord : VariableLengthRecord
    {
        public const int CoreSizeInBytes = 34;
        public const int ItemSizeInBytes = 6;
        public const string LazEncodingUserID = "laszip encoded";
        public const UInt16 LazEncodingRecordID = 22204;
        public const byte PointDataFormatMask = 0x80; // 128; may also be 64 per LASzip dll source code

        public LazCompressorType Compressor { get; set; }
        public LazCoderType Coder { get; set; }

        /// <summary>
        /// Version of .laz tool (e.g. LASzip) used to create .las file.
        /// </summary>
        public Version LazVersion { get; set; }

        public LazOptions Options { get; set; }

        /// <summary>
        /// Chunk size in points.
        /// </summary>
        public Int32 ChunkSize { get; set; }

        /// <summary>
        /// Number of .laz specific extended variable length records.
        /// </summary>
        /// <remarks>
        /// Not used by LASzip. Set to -1 if unused.
        /// </remarks>
        public Int64 NumberOfSpecialEvlrs { get; set; }

        /// <summary>
        /// Offset to .laz specific extended variable length records.
        /// </summary>
        /// <remarks>
        /// Not used by LASzip. Set to -1 if unused.
        /// </remarks>
        public Int64 OffsetToSpecialEvlrs { get; set; }

        /// <summary>
        /// Number of items in a compressed point record. 
        /// </summary>
        /// <remarks>
        /// A compressed point record either corresponds directly to LAS point record format 0, 1, 2, 4, or 6 (<see cref="LazItemType.Point10"/>, 
        /// <see cref="LazItemType.Gpstime11"/>, <see cref="LazItemType.Rgb12"/>, or <see cref="LazItemType.Wavepacket13"/>, 
        /// <see cref="LazItemType.Point14"/> respectively) or consists of a point type plus a set of additional fields which are compressed
        /// separately (e.g. <see cref="LazItemType.Rgb14"/> or <see cref="LazItemType.RgbNir14"/>).
        /// </remarks>
        public UInt16 NumItems { get; set; }

        /// <summary>
        /// Type of compressed items.
        /// </summary>
        public LazItemType[] Type { get; set; }

        /// <summary>
        /// Decompressed size of items in bytes.
        /// </summary>
        public UInt16[] Size { get; set; }

        /// <summary>
        /// Version of .laz compression used with items.
        /// </summary>
        public UInt16[] Version { get; set; }

        public LazVariableLengthRecord(UInt16 reserved, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> vlrBytes)
            : base(reserved, LazVariableLengthRecord.LazEncodingUserID, LazVariableLengthRecord.LazEncodingRecordID, recordLengthAfterHeader, description)
        {
            // read LASzip VLR header
            this.Compressor = (LazCompressorType)BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes);
            this.Coder = (LazCoderType)BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[2..]);
            this.LazVersion = new(vlrBytes[4], vlrBytes[5], 0, BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[6..]));
            this.Options = (LazOptions)BinaryPrimitives.ReadUInt32LittleEndian(vlrBytes[8..]);
            this.ChunkSize = BinaryPrimitives.ReadInt32LittleEndian(vlrBytes[12..]);
            this.NumberOfSpecialEvlrs = BinaryPrimitives.ReadInt64LittleEndian(vlrBytes[16..]);
            this.OffsetToSpecialEvlrs = BinaryPrimitives.ReadInt64LittleEndian(vlrBytes[24..]);
            this.NumItems = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[32..]);

            this.Type = new LazItemType[this.NumItems];
            this.Size = new UInt16[this.NumItems];
            this.Version = new UInt16[this.NumItems];
        }

        public void ReadItem(int itemIndex, ReadOnlySpan<byte> vlrBytes)
        {
            this.Type[itemIndex] = (LazItemType)BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes);
            this.Size[itemIndex] = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[2..]);
            this.Version[itemIndex] = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[4..]);
        }

        public override void Write(Stream stream)
        {
            this.WriteHeader(stream);

            Span<byte> vlrBytes = stackalloc byte[34 + 3 * sizeof(UInt16) * this.NumItems];
            BinaryPrimitives.WriteUInt16LittleEndian(vlrBytes, (UInt16)this.Compressor);
            BinaryPrimitives.WriteUInt16LittleEndian(vlrBytes[2..], (UInt16)this.Coder);
            vlrBytes[4] = (byte)this.LazVersion.Major;
            vlrBytes[5] = (byte)this.LazVersion.Minor;
            BinaryPrimitives.WriteUInt16LittleEndian(vlrBytes[6..], (UInt16)this.LazVersion.Revision);
            BinaryPrimitives.WriteUInt32LittleEndian(vlrBytes[8..], (UInt32)this.Options);
            BinaryPrimitives.WriteInt32LittleEndian(vlrBytes[12..], this.ChunkSize);
            BinaryPrimitives.WriteInt64LittleEndian(vlrBytes[16..], this.NumberOfSpecialEvlrs);
            BinaryPrimitives.WriteInt64LittleEndian(vlrBytes[24..], this.OffsetToSpecialEvlrs);
            BinaryPrimitives.WriteUInt16LittleEndian(vlrBytes[32..], this.NumItems);

            stream.Write(vlrBytes);
            for (int itemIndex = 0, itemOffset = 34; itemIndex < this.NumItems; itemIndex++, itemOffset += 6) 
            {
                BinaryPrimitives.WriteUInt16LittleEndian(vlrBytes[itemOffset..], (UInt16)this.Type[itemIndex]);
                BinaryPrimitives.WriteUInt16LittleEndian(vlrBytes[(itemOffset + 2)..], this.Size[itemIndex]);
                BinaryPrimitives.WriteUInt16LittleEndian(vlrBytes[(itemOffset + 4)..], this.Version[itemIndex]);
            }
        }
    }
}
