using System;
using System.IO;
using System.Numerics;

namespace Mars.Clouds.Las
{
    public abstract class VariableLengthRecordBase<TRecordLength> where TRecordLength : INumber<TRecordLength>
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
        /// Length of record in bytes, excluding the 54 header bytes.
        /// </summary>
        public TRecordLength RecordLengthAfterHeader { get; set; }

        /// <summary>
        /// UTF8 description of record.
        /// </summary>
        public string Description { get; set; }

        protected VariableLengthRecordBase(UInt16 reserved, string userID, UInt16 recordID, TRecordLength recordLengthAfterHeader, string description)
        {
            if (description.Length > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(description), "Maximum length of a variable length record's description is 32 UTF8 characters.");
            }

            this.Reserved = reserved;
            this.UserID = userID;
            this.RecordID = recordID;
            this.RecordLengthAfterHeader = recordLengthAfterHeader;
            this.Description = description;
        }

        public abstract void Write(Stream stream);
    }
}
