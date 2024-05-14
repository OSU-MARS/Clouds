using System;
using System.IO;

namespace Mars.Clouds.Las
{
    public class ExtendedVariableLengthRecordUntyped : ExtendedVariableLengthRecord
    {
        public byte[] Data { get; set; }

        public ExtendedVariableLengthRecordUntyped(UInt16 reserved, string userID, UInt16 recordID, UInt64 recordLengthAfterHeader, string description, byte[] data)
            : base(reserved, userID, recordID, recordLengthAfterHeader, description)
        {
            this.Data = data;
        }

        public override void Write(Stream stream)
        {
            this.WriteHeader(stream);
            stream.Write(this.Data);
        }
    }
}
