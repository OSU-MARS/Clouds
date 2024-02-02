using System;

namespace Mars.Clouds.Las
{
    [Flags]
    public enum ScanFlags : byte
    {
        None = 0x0,
        ScanDirection = 0x1,
        EdgeOfFlightLine = 0x2,
        Overlap = 0x4 // LAS 1.4
    }
}
