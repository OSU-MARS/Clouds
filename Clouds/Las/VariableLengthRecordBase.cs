using System;
using System.Numerics;

namespace Mars.Clouds.Las
{
    public class VariableLengthRecordBase
    {
        /// <summary>
        /// Variable length record signature in LAS 1.0, reserved subsequently and set to zero in LAS 1.4+.
        /// </summary>
        public UInt16 Reserved { get; set; }

        /// <summary>
        /// ASCII string identifying user who created the record.
        /// </summary>
        public string UserID { get; set; }

        /// <summary>
        /// Variable length record signature in LAS 1.0, reserved subsequently and set to zero in LAS 1.4+.
        /// </summary>
        public UInt16 RecordID { get; set; }

        /// <summary>
        /// UTF8 description of record.
        /// </summary>
        public string Description { get; set; }

        protected VariableLengthRecordBase(UInt16 reserved, string userID, UInt16 recordID, string description)
        {
            this.Reserved = reserved;
            this.UserID = userID;
            this.RecordID = recordID;
            this.Description = description;
        }
    }

    public class VariableLengthRecordBase<TRecordLength> : VariableLengthRecordBase where TRecordLength : INumber<TRecordLength>
    {
        /// <summary>
        /// Length of record in bytes, excluding the 54 header bytes.
        /// </summary>
        public TRecordLength RecordLengthAfterHeader { get; set; }

        protected VariableLengthRecordBase(UInt16 reserved, string userID, UInt16 recordID, TRecordLength recordLengthAfterHeader, string description)
            : base(reserved, userID, recordID, description)
        {
            this.RecordLengthAfterHeader = recordLengthAfterHeader;
        }
    }
}
