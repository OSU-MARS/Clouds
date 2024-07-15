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
        AerialMean = 0x008,
        GroundMean = 0x010,

        // diagnostic bands: point counts
        AerialPoints = 0x020,
        GroundPoints = 0x040,

        // diagnostic bands: source IDs
        SourceIDSurface = 0x080,

        // band groups
        Required = Surface | CanopyMaxima3 | CanopyHeight,
        DiagnosticZ = AerialMean | GroundMean,
        PointCounts = AerialPoints | GroundPoints
    }
}
