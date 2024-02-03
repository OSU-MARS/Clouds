using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System.Numerics;

namespace Mars.Clouds.Las
{
    public class ImageRaster<TPixel> : Raster<TPixel> where TPixel : INumber<TPixel>
    {
        // first returns
        public RasterBand<TPixel> Red { get; private init; }
        public RasterBand<TPixel> Green { get; private init; }
        public RasterBand<TPixel> Blue { get; private init; }
        public RasterBand<TPixel> NearInfrared { get; private init; }
        public RasterBand<TPixel> Intensity { get; private init; }

        // second return intensity and point counts
        public RasterBand<TPixel> IntensitySecondReturn { get; private init; }
        public RasterBand<TPixel> FirstReturns { get; private init; }
        public RasterBand<TPixel> SecondReturns { get; private init; }

        public ImageRaster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, TPixel noDataValue)
            : base(crs, transform, xSize, ySize, 8)
        {
            this.Red = this.Bands[0];
            this.Green = this.Bands[1];
            this.Blue = this.Bands[2];
            this.NearInfrared = this.Bands[3];
            this.Intensity = this.Bands[4];
            this.IntensitySecondReturn = this.Bands[5];
            this.FirstReturns = this.Bands[6];
            this.SecondReturns = this.Bands[7];

            this.Red.Name = "red";
            this.Green.Name = "green";
            this.Blue.Name = "blue";
            this.NearInfrared.Name = "nir";
            this.Intensity.Name = "intensity";
            this.IntensitySecondReturn.Name = "intensitySecondReturn";
            this.FirstReturns.Name = "firstReturns";
            this.SecondReturns.Name = "secondReturns";

            this.SetNoDataOnAllBands(noDataValue);

            // leave all bands at default value of zero; no datas are filled in OnPointAdditionComplete()
        }

        public void OnPointAdditionComplete()
        {
            TPixel redNoData = this.Red.NoDataValue;
            TPixel greenNoData = this.Green.NoDataValue;
            TPixel blueNoData = this.Blue.NoDataValue;
            TPixel nearInfraredNoData = this.NearInfrared.NoDataValue;
            TPixel intensityNoData = this.Intensity.NoDataValue;
            TPixel intensitySecondReturnNoData = this.IntensitySecondReturn.NoDataValue;

            for (int index = 0; index < this.CellsPerBand; ++index)
            {
                TPixel firstReturns = this.FirstReturns[index];
                if (firstReturns != TPixel.Zero)
                {
                    this.Red[index] /= firstReturns;
                    this.Green[index] /= firstReturns;
                    this.Blue[index] /= firstReturns;
                    this.NearInfrared[index] /= firstReturns;
                    this.Intensity[index] /= firstReturns;
                }
                else
                {
                    this.Red[index] = redNoData;
                    this.Green[index] = greenNoData;
                    this.Blue[index] = blueNoData;
                    this.NearInfrared[index] = nearInfraredNoData;
                    this.Intensity[index] = intensityNoData;
                }

                TPixel secondReturns = this.SecondReturns[index];
                if (secondReturns != TPixel.Zero)
                {
                    this.IntensitySecondReturn[index] /= secondReturns;
                }
                else
                {
                    this.IntensitySecondReturn[index] = intensitySecondReturnNoData;
                }
            }
        }
    }
}
