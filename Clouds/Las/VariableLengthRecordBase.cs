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

        protected VariableLengthRecordBase()
        {
            this.Reserved = 0;
            this.UserID = String.Empty;
            this.Description = String.Empty;
        }
    }

    public class VariableLengthRecordBase<TRecordLength> : VariableLengthRecordBase where TRecordLength : INumber<TRecordLength>
    {
        /// <summary>
        /// Length of record in bytes, excluding the 54 header bytes.
        /// </summary>
        public TRecordLength RecordLengthAfterHeader { get; set; }

        public VariableLengthRecordBase()
        {
            this.RecordLengthAfterHeader = TRecordLength.Zero;
        }
    }
}
