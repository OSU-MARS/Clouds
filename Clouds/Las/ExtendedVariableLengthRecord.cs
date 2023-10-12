using System;

namespace Mars.Clouds.Las
{
    internal class ExtendedVariableLengthRecord : VariableLengthRecordBase<UInt64>
    {
        public byte[] Data { get; set; }

        public ExtendedVariableLengthRecord()
        {
            this.Data = Array.Empty<byte>();
        }
    }
}
