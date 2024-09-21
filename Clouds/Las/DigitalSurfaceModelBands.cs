using System;

namespace Mars.Clouds.Las
{
    [Flags]
    public enum DigitalSurfaceModelBands : UInt32
    {
        None = 0x0000,

        // primary bands
        Surface = 0x0001,
        CanopyMaxima3 = 0x0002,
        CanopyHeight = 0x0004,

        // slope and aspect bands
        DsmSlope = 0x0008,
        DsmAspect = 0x0010,
        CmmSlope3 = 0x0020,
        CmmAspect3 = 0x0040,

        // diagnostic bands in z
        Subsurface = 0x0080,
        AerialMean = 0x0100,
        GroundMean = 0x0200,

        // diagnostic bands: point counts
        AerialPoints = 0x0400,
        GroundPoints = 0x0800,

        // diagnostic bands: source IDs
        ReturnNumberSurface = 0x1000,
        SourceIDSurface = 0x2000,

        // band groups
        Primary = Surface | CanopyMaxima3 | CanopyHeight,
        SlopeAspect = DsmSlope | DsmAspect | CmmSlope3 | CmmAspect3,
        DiagnosticZ = Subsurface | AerialMean | GroundMean,
        PointCounts = AerialPoints | GroundPoints,

        Default = Surface | CanopyMaxima3 | CanopyHeight | AerialMean | GroundMean | PointCounts | SourceIDSurface
    }
}
