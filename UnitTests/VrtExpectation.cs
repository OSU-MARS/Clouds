using Mars.Clouds.Vrt;
using OSGeo.GDAL;
using System;

namespace Mars.Clouds.UnitTests
{
    internal class VrtExpectation
    {
        public int BandCount { get; init; }
        public double OriginX { get; init; }
        public double OriginY { get; init; }
        public int RasterXSize { get; init; }
        public int RasterYSize { get; init; }
        public DataType[] BandType { get; init; }
        public int[] SourceBandIndex { get; init; }
        public ColorInterpretation[] ColorInterpretation { get; init; }
        public double?[] NoDataValue { get; init; }
        public int[] Histograms { get; init; }
        public int[] Metadata { get; init; }
        public int Sources { get; init; }

        public VrtExpectation()
        {
            this.BandCount = -1;
            this.OriginX = Double.NaN;
            this.OriginY = Double.NaN;
            this.RasterXSize = -1;
            this.RasterYSize = -1;
            this.BandType = [];
            this.SourceBandIndex = [];
            this.ColorInterpretation = [];
            this.NoDataValue = [];
            this.Histograms = [];
            this.Metadata = [];
            this.Sources = -1;
        }
    }
}
