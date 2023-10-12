namespace Mars.Clouds.Las
{
    public enum PointClassification : byte
    {
        // LAS 1.4 R15 Table 9: point types 0-5, Table 17: point types 6-10
        NeverClassified = 0, // all point types
        Unclassified = 1, // all point types
        Ground = 2, // all point types
        LowVegetation = 3, // all point types
        MediumVegitation = 4, // all point types
        HighVegetation = 5, // all point types
        Building = 6, // all point types
        LowPointNoise = 7, // all point types
        ModelKeyPoint = 8, // reserved for point types 6-10
        Water = 9, // all point types
        Rail = 10, // point types 6-10
        RoadSurface = 11, // point types 6-10
        OverlapPoints = 12, // reserved for point types 6-10
        WireGuard = 13, // point types 6-10
        WireConductor = 14, // point types 6-10
        TransmissionTower = 15, // point types 6-10
        WireStructureConnector = 16, // point types 6-10
        BridgeDeck = 17, // point types 6-10
        HighNoise = 18, // point types 6-10
        OverheadStructure = 19, // point types 6-10
        IgnoredGround = 20, // point types 6-10
        Snow = 21 // point types 6-10
    }
}
