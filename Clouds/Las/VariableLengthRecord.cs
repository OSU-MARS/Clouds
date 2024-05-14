using System;
using System.Buffers.Binary;
using System.IO;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// Any variable length record which is not defined in LAS 1.4 R15.
    /// </summary>
    public abstract class VariableLengthRecord(UInt16 reserved, string userID, UInt16 recordID, UInt16 recordLengthAfterHeader, string description) : VariableLengthRecordBase<UInt16>(reserved, userID, recordID, recordLengthAfterHeader, description)
    {
        public const UInt16 HeaderSizeInBytes = 54; // since RecordLengthAfterHeader is two bytes (LAS 1.4 R15 §2.5)

        protected void WriteHeader(Stream stream)
        { 
            Span<byte> header = stackalloc byte[VariableLengthRecord.HeaderSizeInBytes];
            BinaryPrimitives.WriteUInt16LittleEndian(header, this.Reserved);
            LasWriter.WriteNullTerminated(header[2..], this.UserID, 16);
            BinaryPrimitives.WriteUInt16LittleEndian(header[18..], this.RecordID);
            BinaryPrimitives.WriteUInt16LittleEndian(header[20..], this.RecordLengthAfterHeader);
            LasWriter.WriteNullTerminated(header[22..], this.Description, 32);

            stream.Write(header);
        }
    }
}
