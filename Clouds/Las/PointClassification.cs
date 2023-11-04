namespace Mars.Clouds.Las
{
    public enum PointClassification : byte
    {
        // LAS 1.4 R15 Table 5 (point formats 0-5) and Table 17 (point formats 6-10)
        NeverClassified = 0,
        Unclassified = 1,
        Ground = 2,
        LowVegetation = 3,
        MediumVegetation = 4,
        HighVegetation = 5,
        Building = 6,
        LowNoise = 7, // low point
        ModelKeyPoint = 8, // mass point, reserved for point types 6-10
        Water = 9,
        Rail = 10, // reserved for point types 0-5
        RoadSurface = 11, // reserved for point types 0-5
        OverlapPoint = 12, // reserved for point types 6-10
        // values of 13 or greater reserved for point types 0-5
        WireGuard = 13, // shield
        WireConductor = 14, // phase
        TransmissionTower = 15,
        WireStructureConnector = 16, // e.g., insulators
        BridgeDeck = 17,
        HighNoise = 18,
        OverheadStructure = 19, // e.g., conveyors, mining equipment, traffic lights
        IgnoredGround = 20, // e.g., breakline proximity
        Snow = 21,
        TemporalExclusion = 22 // Features excluded due to changes over time between data sources – e.g., water levels, landslides, permafrost
    }
}
