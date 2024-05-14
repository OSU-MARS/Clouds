using System;
using System.Buffers.Binary;
using System.IO;

namespace Mars.Clouds.Las
{
    public abstract class ExtendedVariableLengthRecord(UInt16 reserved, string userID, UInt16 recordID, UInt64 recordLengthAfterHeader, string description) : VariableLengthRecordBase<UInt64>(reserved, userID, recordID, recordLengthAfterHeader, description)
    {
        public const UInt16 HeaderSizeInBytes = 60; // since RecordLengthAfterHeader is eight bytes (LAS 1.4 R15 §2.7)

        protected void WriteHeader(Stream stream)
        {
            Span<byte> header = stackalloc byte[ExtendedVariableLengthRecord.HeaderSizeInBytes];
            BinaryPrimitives.WriteUInt16LittleEndian(header, this.Reserved);
            LasWriter.WriteNullTerminated(header[2..], this.UserID, 16);
            BinaryPrimitives.WriteUInt16LittleEndian(header[18..], this.RecordID);
            BinaryPrimitives.WriteUInt64LittleEndian(header[20..], this.RecordLengthAfterHeader);
            LasWriter.WriteNullTerminated(header[22..], this.Description, 32);
        }
    }
}
