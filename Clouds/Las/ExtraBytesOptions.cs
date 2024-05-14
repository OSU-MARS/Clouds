using System;

namespace Mars.Clouds.Las
{
    [Flags]
    public enum ExtraBytesOptions : byte
    {
        NoData = 0x01,
        Min = 0x02,
        Max = 0x04,
        Scale = 0x08,
        Offset = 0x10
    }
}
