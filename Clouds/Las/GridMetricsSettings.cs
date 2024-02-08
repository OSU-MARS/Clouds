namespace Mars.Clouds.Las
{
    public class GridMetricsSettings
    {
        public bool IntensityPCumulativeZQ { get; set; }
        public bool IntensityTotal { get; set; }
        public bool Kurtosis { get; set; }
        public bool PointBoundingArea { get; set; }
        public bool ZPCumulative { get; set; }

        public GridMetricsSettings() 
        {
            this.IntensityPCumulativeZQ = false;
            this.IntensityTotal = false;
            this.Kurtosis = false;
            this.PointBoundingArea = false;
            this.ZPCumulative = false;
        }
    }
}
