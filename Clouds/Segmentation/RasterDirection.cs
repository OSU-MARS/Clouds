using System;

namespace Mars.Clouds.Segmentation
{
    [Flags]
    public enum RasterDirection : byte
    {
        None = 0x0,
        North = 0x1,
        South = 0x2,
        East = 0x4,
        West = 0x8,

        Northwest = North | West,
        Northeast = North | East,
        Southwest = South | West,
        Southeast = South | East
    }
}
