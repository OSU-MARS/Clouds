using System;

namespace Mars.Clouds.Las
{
    [Flags]
    public enum PointClassificationFlags : byte
    {
        None = 0x0,
        Synthetic = 0x1,
        KeyPoint = 0x2,
        Withheld = 0x4 // LAS 1.4 R15 Tables 8 (point formats 0-5) and 16 (point format 6+)
    }
}
