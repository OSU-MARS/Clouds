namespace Mars.Clouds.Las
{
    public class GridMetricsSettings
    {
        public bool IntensityPCumulativeZQ { get; set; }
        public bool IntensityTotal { get; set; }
        public bool PointBoundingArea { get; set; }

        public GridMetricsSettings() 
        {
            this.IntensityPCumulativeZQ = false;
            this.IntensityTotal = false;
            this.PointBoundingArea = false;
        }
    }
}
