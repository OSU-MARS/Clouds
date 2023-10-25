using System;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// Any variable length record which is not defined in LAS 1.4 R15.
    /// </summary>
    public class VariableLengthRecord : VariableLengthRecordBase<UInt16>
    {
        public byte[] Data { get; set; }

        public VariableLengthRecord(UInt16 reserved, string userID, UInt16 recordID, UInt16 recordLengthAfterHeader, string description, byte[] data) 
            : base(reserved, userID, recordID, recordLengthAfterHeader, description)
        { 
            this.Data = data;
        }
    }
}
