namespace Mars.Clouds.Cmdlets.Drives
{
    internal readonly struct PhysicalDisk
    {
        public BusType BusType { get; init; }
        public MediaType MediaType { get; init; }
    }
}
