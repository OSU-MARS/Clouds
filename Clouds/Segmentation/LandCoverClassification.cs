namespace Mars.Clouds.Segmentation
{
    public enum LandCoverClassification : byte
    {
        Unclassified = 0,
        Bare = 1,
        BareShadow = 2,
        BrownTree = 3,
        GreyTree = 4,
        Conifer = 5,
        ConiferShadow = 6,
        ConiferDeepShadow = 7,
        Hardwood = 8,
        HardwoodShadow = 9,
        HardwoodDeepShadow = 10,

        MaxValue = HardwoodDeepShadow
    }
}
