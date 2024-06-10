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
        Layer1 = 0x008,
        Layer2 = 0x010,
        Ground = 0x020,

        // diagnostic bands: point counts
        AerialPoints = 0x040,
        GroundPoints = 0x080,

        // diagnostic bands: source IDs
        SourceIDSurface = 0x100,
        SourceIDLayer1 = 0x200,
        SourceIDLayer2 = 0x400,

        // band groups
        Required = Surface | CanopyMaxima3 | CanopyHeight,
        DiagnosticZ = Layer1 | Layer2 | Ground,
        PointCounts = AerialPoints | GroundPoints,
        SourceIDs = SourceIDSurface | SourceIDLayer1 | SourceIDLayer2
    }
}
