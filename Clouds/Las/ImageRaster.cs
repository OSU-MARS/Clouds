using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;
using System.Numerics;

namespace Mars.Clouds.Las
{
    public class ImageRaster<TPixel> : Raster<TPixel> where TPixel : IMinMaxValue<TPixel>, INumber<TPixel>
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

        public ImageRaster(Grid extent, TPixel noDataValue)
            : this(extent.Crs, extent.Transform, extent.SizeX, extent.SizeY, noDataValue)
        {
        }

        // leave all bands at default value of zero; no datas are filled in OnPointAdditionComplete()
        public ImageRaster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, TPixel noDataValue)
            : base(crs, transform, xSize, ySize, [ "red", "green", "blue", "nir", "intensity", "intensitySecondReturn", "firstReturns", "secondReturns" ], noDataValue, initializeBandsToNoData: false)
        {
            this.Red = this.Bands[0];
            this.Green = this.Bands[1];
            this.Blue = this.Bands[2];
            this.NearInfrared = this.Bands[3];
            this.Intensity = this.Bands[4];
            this.IntensitySecondReturn = this.Bands[5];
            this.FirstReturns = this.Bands[6];
            this.SecondReturns = this.Bands[7];
        }

        /// <summary>
        /// Convert to 16 bit unsigned image. Input data must be in the 16 bit range of 0–65,535.
        /// </summary>
        /// <remarks>
        /// The no data value is 65,535. Input values of 65,535 are therefore reduced to 65,534.
        /// </remarks>
        public ImageRaster<UInt16> AsUInt16()
        {
            UInt16 noData16 = UInt16.MaxValue;
            UInt16 maxData16 = UInt16.MaxValue - 1;
            ImageRaster<UInt16> image16 = new(this, noData16);

            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                TPixel firstReturns = this.FirstReturns[cellIndex];
                if (firstReturns != TPixel.Zero)
                {
                    TPixel red = this.Red[cellIndex];
                    UInt16 red16 = this.Red.IsNoData(red) ? noData16 : UInt16.Min(UInt16.CreateChecked(red), maxData16);
                    image16.Red[cellIndex] = red16;

                    TPixel green = this.Green[cellIndex];
                    UInt16 green16 = this.Green.IsNoData(green) ? noData16 : UInt16.Min(UInt16.CreateChecked(green), noData16);
                    image16.Green[cellIndex] = green16;

                    TPixel blue = this.Blue[cellIndex];
                    UInt16 blue16 = this.Blue.IsNoData(blue) ? noData16 : UInt16.Min(UInt16.CreateChecked(blue), noData16);
                    image16.Blue[cellIndex] = blue16;

                    TPixel nir = this.NearInfrared[cellIndex];
                    UInt16 nir16 = this.NearInfrared.IsNoData(nir) ? noData16 : UInt16.Min(UInt16.CreateChecked(nir), noData16);
                    image16.NearInfrared[cellIndex] = nir16;

                    TPixel intensity = this.Intensity[cellIndex];
                    UInt16 intensity16 = this.Intensity.IsNoData(intensity) ? noData16 : UInt16.Min(UInt16.CreateChecked(intensity), noData16);
                    image16.Intensity[cellIndex] = intensity16;
                }
                else
                {
                    image16.Red[cellIndex] = noData16;
                    image16.Green[cellIndex] = noData16;
                    image16.Blue[cellIndex] = noData16;
                    image16.NearInfrared[cellIndex] = noData16;
                    image16.Intensity[cellIndex] = noData16;
                }

                TPixel secondReturns = this.FirstReturns[cellIndex];
                if (secondReturns != TPixel.Zero)
                {
                    TPixel intensitySecondReturn = this.IntensitySecondReturn[cellIndex];
                    UInt16 intensitySecondReturn16 = this.IntensitySecondReturn.IsNoData(intensitySecondReturn) ? noData16 : UInt16.Min(UInt16.CreateChecked(intensitySecondReturn), noData16);
                    image16.IntensitySecondReturn[cellIndex] = intensitySecondReturn16;
                }
                else
                {
                    image16.IntensitySecondReturn[cellIndex] = noData16;
                }
            }

            return image16;
        }

        /// <summary>
        /// Convert to 32 bit unsigned image. Input data must be in the 32 bit range of 0–4,294,967,296.
        /// </summary>
        /// <remarks>
        /// The no data value is 4,294,967,296. Input values of 4,294,967,296 are therefore reduced to 4,294,967,295.
        /// </remarks>
        // copy+paste of AsUInt16() + find-replace
        public ImageRaster<UInt32> AsUInt32()
        {
            UInt32 noData32 = UInt32.MaxValue;
            UInt32 maxData32 = UInt32.MaxValue - 1;
            ImageRaster<UInt32> image32 = new(this, noData32);

            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                TPixel firstReturns = this.FirstReturns[cellIndex];
                if (firstReturns != TPixel.Zero)
                {
                    TPixel red = this.Red[cellIndex];
                    UInt32 red32 = this.Red.IsNoData(red) ? noData32 : UInt32.Min(UInt32.CreateChecked(red), maxData32);
                    image32.Red[cellIndex] = red32;

                    TPixel green = this.Green[cellIndex];
                    UInt32 green32 = this.Green.IsNoData(green) ? noData32 : UInt32.Min(UInt32.CreateChecked(green), noData32);
                    image32.Green[cellIndex] = green32;

                    TPixel blue = this.Blue[cellIndex];
                    UInt32 blue32 = this.Blue.IsNoData(blue) ? noData32 : UInt32.Min(UInt32.CreateChecked(blue), noData32);
                    image32.Blue[cellIndex] = blue32;

                    TPixel nir = this.NearInfrared[cellIndex];
                    UInt32 nir32 = this.NearInfrared.IsNoData(nir) ? noData32 : UInt32.Min(UInt32.CreateChecked(nir), noData32);
                    image32.NearInfrared[cellIndex] = nir32;

                    TPixel intensity = this.Intensity[cellIndex];
                    UInt32 intensity32 = this.Intensity.IsNoData(intensity) ? noData32 : UInt32.Min(UInt32.CreateChecked(intensity), noData32);
                    image32.Intensity[cellIndex] = intensity32;
                }
                else
                {
                    image32.Red[cellIndex] = noData32;
                    image32.Green[cellIndex] = noData32;
                    image32.Blue[cellIndex] = noData32;
                    image32.NearInfrared[cellIndex] = noData32;
                    image32.Intensity[cellIndex] = noData32;
                }

                TPixel secondReturns = this.FirstReturns[cellIndex];
                if (secondReturns != TPixel.Zero)
                {
                    TPixel intensitySecondReturn = this.IntensitySecondReturn[cellIndex];
                    UInt32 intensitySecondReturn32 = this.IntensitySecondReturn.IsNoData(intensitySecondReturn) ? noData32 : UInt32.Min(UInt32.CreateChecked(intensitySecondReturn), noData32);
                    image32.IntensitySecondReturn[cellIndex] = intensitySecondReturn32;
                }
                else
                {
                    image32.IntensitySecondReturn[cellIndex] = noData32;
                }
            }

            return image32;
        }

        /// <summary>
        /// Convert accumulated red, green, blue, NIR, first return intensity, and second return intensity sums to averages.
        /// </summary>
        public void OnPointAdditionComplete()
        {
            TPixel redNoData = this.Red.NoDataValue;
            TPixel greenNoData = this.Green.NoDataValue;
            TPixel blueNoData = this.Blue.NoDataValue;
            TPixel nearInfraredNoData = this.NearInfrared.NoDataValue;
            TPixel intensityNoData = this.Intensity.NoDataValue;
            TPixel intensitySecondReturnNoData = this.IntensitySecondReturn.NoDataValue;

            for (int index = 0; index < this.Cells; ++index)
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
