using System;

namespace Mars.Clouds.Las
{
    public class LasWriteTransformedResult
    {
        public double MaxX { get; init; }
        public double MaxY { get; init; }
        public double MinX { get; init; }
        public double MinY { get; init; }
        public long ReturnNumbersRepaired { get; init; }

        public LasWriteTransformedResult()
        {
            this.MaxX = Double.NaN;
            this.MaxY = Double.NaN;
            this.MinX = Double.NaN;
            this.MinY = Double.NaN;
            this.ReturnNumbersRepaired = 0;
        }
    }
}
