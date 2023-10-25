using System;

namespace Mars.Clouds.Las
{
    internal class ExtendedVariableLengthRecord : VariableLengthRecordBase<UInt64>
    {
        public byte[] Data { get; set; }

        public ExtendedVariableLengthRecord(UInt16 reserved, string userID, UInt16 recordID, UInt64 recordLengthAfterHeader, string description, byte[] data)
            : base(reserved, userID, recordID, recordLengthAfterHeader, description)
        {
            this.Data = data;
        }
    }
}
