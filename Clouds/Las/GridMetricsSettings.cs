namespace Mars.Clouds.Las
{
    public class GridMetricsSettings
    {
        public bool IntensityPCumulativeZQ { get; set; }
        public bool IntensityPGround { get; set; }
        public bool IntensityTotal { get; set; }
        public bool Kurtosis { get; set; }
        public bool ZPCumulative { get; set; }
        public bool ZQFives { get; set; }

        public GridMetricsSettings() 
        {
            this.IntensityPCumulativeZQ = false;
            this.IntensityPGround = false;
            this.IntensityTotal = false;
            this.Kurtosis = false;
            this.ZPCumulative = false;
            this.ZQFives = false;
        }
    }
}
