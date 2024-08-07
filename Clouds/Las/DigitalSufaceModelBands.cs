using System;

namespace Mars.Clouds.Las
{
    [Flags]
    public enum DigitalSufaceModelBands
    {
        None = 0x000,

        // required bands
        Surface = 0x001,
        CanopyMaxima3 = 0x002,
        CanopyHeight = 0x004,

        // diagnostic bands in z
        Subsurface = 0x008,
        AerialMean = 0x010,
        GroundMean = 0x020,

        // diagnostic bands: point counts
        AerialPoints = 0x040,
        GroundPoints = 0x080,

        // diagnostic bands: source IDs
        ReturnNumberSurface = 0x100,
        SourceIDSurface = 0x200,

        // band groups
        Required = Surface | CanopyMaxima3 | CanopyHeight,
        DiagnosticZ = Subsurface | AerialMean | GroundMean,
        PointCounts = AerialPoints | GroundPoints,

        Default = Surface | CanopyMaxima3 | CanopyHeight | AerialMean | GroundMean | PointCounts | SourceIDSurface
    }
}
