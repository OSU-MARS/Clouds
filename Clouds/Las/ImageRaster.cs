using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Mars.Clouds.Las
{
    public class ImageRaster<TPixel> : Raster where TPixel : IMinMaxValue<TPixel>, INumber<TPixel>
    {
        // first returns
        public RasterBand<TPixel> Red { get; private init; }
        public RasterBand<TPixel> Green { get; private init; }
        public RasterBand<TPixel> Blue { get; private init; }
        public RasterBand<TPixel>? NearInfrared { get; private init; }
        public RasterBand<TPixel> Intensity { get; private init; }

        // second return intensity and point counts
        public RasterBand<TPixel> IntensitySecondReturn { get; private init; }
        public RasterBand<TPixel> FirstReturns { get; private init; }
        public RasterBand<TPixel> SecondReturns { get; private init; }

        // create for integer narrowing of self
        private ImageRaster(Grid extent, bool includeNearInfrared)
            : this(extent.Crs, extent.Transform, extent.SizeX, extent.SizeY, includeNearInfrared, RasterBandInitialValue.Unintialized)
        {
            // bands are unitialized as data will be transferred from originating ImageRaster
        }

        // create from point cloud tile and cell size for accumulation of mean point RGB[+NIR] intensity values in pixels
        public ImageRaster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, bool includeNearInfrared)
            : this(crs, transform, xSize, ySize, includeNearInfrared, RasterBandInitialValue.Default)
        {
            // both intensity bands and RGB+NIR are set to zero for accumulation to mean
            // No datas are filled in OnPointAdditionComplete().
        }

        private ImageRaster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, bool includeNearInfrared, RasterBandInitialValue initialValue)
            : base(crs, transform, xSize, ySize)
        {
            TPixel noDataValue = RasterBand<TPixel>.GetDefaultNoDataValue();

            this.Red = new(this, "red", noDataValue, initialValue);
            this.Green = new(this, "green", noDataValue, initialValue);
            this.Blue = new(this, "blue", noDataValue, initialValue);
            this.NearInfrared = includeNearInfrared ? new(this, "nir", noDataValue, initialValue) : null;
            this.Intensity = new(this, "intensity", noDataValue, initialValue);

            this.IntensitySecondReturn = new(this, "intensitySecondReturn", noDataValue, initialValue);
            this.FirstReturns = new(this, "firstReturn", initialValue); // count of zero is valid: do not set a no data value
            this.SecondReturns = new(this, "secondReturn", initialValue);
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.Red;
            yield return this.Green;
            yield return this.Blue;
            if (this.NearInfrared != null)
            {
                yield return this.NearInfrared;
            }

            yield return this.Intensity;
            yield return this.IntensitySecondReturn;
            yield return this.FirstReturns;
            yield return this.SecondReturns;
        }

        public override int GetBandIndex(string name)
        {
            if (String.Equals(name, this.Red.Name, StringComparison.Ordinal))
            {
                return 0;
            }
            if (String.Equals(name, this.Green.Name, StringComparison.Ordinal))
            {
                return 1;
            }
            if (String.Equals(name, this.Blue.Name, StringComparison.Ordinal))
            {
                return 2;
            }

            int nearInfraredOffset = 0;
            if (this.NearInfrared != null)
            {
                if (String.Equals(name, this.NearInfrared.Name, StringComparison.Ordinal))
                {
                    return 3;
                }

                nearInfraredOffset = 1;
            }

            if (String.Equals(name, this.Intensity.Name, StringComparison.Ordinal))
            {
                return 3 + nearInfraredOffset;
            }

            if (String.Equals(name, this.IntensitySecondReturn.Name, StringComparison.Ordinal))
            {
                return 4 + nearInfraredOffset;
            }
            if (String.Equals(name, this.FirstReturns.Name, StringComparison.Ordinal))
            {
                return 5 + nearInfraredOffset;
            }
            if (String.Equals(name, this.SecondReturns.Name, StringComparison.Ordinal))
            {
                return 6 + nearInfraredOffset;
            }

            throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
        }

        /// <summary>
        /// Convert accumulated red, green, blue, NIR, first return intensity, and second return intensity sums to averages.
        /// </summary>
        public void OnPointAdditionComplete()
        {
            // all bands are of the same type and ones bearing no data should all have the same no data value
            TPixel noDataValue = this.Red.NoDataValue;
            Debug.Assert((this.Green.NoDataValue == noDataValue) && (this.Blue.NoDataValue == noDataValue) && ((this.NearInfrared == null) || (this.NearInfrared.NoDataValue == noDataValue)) && (this.Intensity.NoDataValue == noDataValue) && (this.IntensitySecondReturn.NoDataValue == noDataValue));

            for (int index = 0; index < this.Cells; ++index)
            {
                TPixel firstReturns = this.FirstReturns[index];
                if (firstReturns != TPixel.Zero)
                {
                    this.Intensity[index] /= firstReturns;
                    this.Red[index] /= firstReturns;
                    this.Green[index] /= firstReturns;
                    this.Blue[index] /= firstReturns;
                    if (this.NearInfrared != null)
                    {
                        this.NearInfrared[index] /= firstReturns;
                    }
                }
                else
                {
                    this.Intensity[index] = noDataValue;
                    this.Red[index] = noDataValue;
                    this.Green[index] = noDataValue;
                    this.Blue[index] = noDataValue;
                    if (this.NearInfrared != null)
                    {
                        this.NearInfrared[index] = noDataValue;
                    }
                }

                TPixel secondReturns = this.SecondReturns[index];
                if (secondReturns != TPixel.Zero)
                {
                    this.IntensitySecondReturn[index] /= secondReturns;
                }
                else
                {
                    this.IntensitySecondReturn[index] = noDataValue;
                }
            }
        }

        /// <summary>
        /// Convert to 16 bit unsigned image. Input data must be in the 16 bit range of UInt16.MinValue to UInt16.MaxValue.
        /// </summary>
        public ImageRaster<UInt16> PackToUInt16()
        {
            bool hasNearInfrared = this.NearInfrared != null;
            ImageRaster<UInt16> image16 = new(this, hasNearInfrared);

            DataTypeExtensions.Pack(this.Red.Data, image16.Red.Data, noDataSaturatingFromAbove: true);
            DataTypeExtensions.Pack(this.Green.Data, image16.Green.Data, noDataSaturatingFromAbove: true);
            DataTypeExtensions.Pack(this.Blue.Data, image16.Blue.Data, noDataSaturatingFromAbove: true);
            if (hasNearInfrared)
            {
                Debug.Assert((this.NearInfrared != null) && (image16.NearInfrared != null));
                DataTypeExtensions.Pack(this.NearInfrared.Data, image16.NearInfrared.Data, noDataSaturatingFromAbove: true);
            }
            DataTypeExtensions.Pack(this.Intensity.Data, image16.Intensity.Data, noDataSaturatingFromAbove: true);

            DataTypeExtensions.Pack(this.IntensitySecondReturn.Data, image16.IntensitySecondReturn.Data, noDataSaturatingFromAbove: true);
            DataTypeExtensions.Pack(this.FirstReturns.Data, image16.FirstReturns.Data, noDataSaturatingFromAbove: false);
            DataTypeExtensions.Pack(this.SecondReturns.Data, image16.SecondReturns.Data, noDataSaturatingFromAbove: false);

            return image16;
        }

        /// <summary>
        /// Convert to 32 bit unsigned image. Input data must be in the range of UInt32.MinValue to UInt32.MaxValue.
        /// </summary>
        // TODO: write DataTypeExtension methods needed to factor this and AsUInt16() into a common generic method.
        public ImageRaster<UInt32> PackToUInt32()
        {
            bool hasNearInfrared = this.NearInfrared != null;
            ImageRaster<UInt32> image32 = new(this, hasNearInfrared);

            DataTypeExtensions.Pack(this.Red.Data, image32.Red.Data, noDataSaturatingFromAbove: true);
            DataTypeExtensions.Pack(this.Green.Data, image32.Green.Data, noDataSaturatingFromAbove: true);
            DataTypeExtensions.Pack(this.Blue.Data, image32.Blue.Data, noDataSaturatingFromAbove: true);
            if (hasNearInfrared)
            {
                Debug.Assert((this.NearInfrared != null) && (image32.NearInfrared != null));
                DataTypeExtensions.Pack(this.NearInfrared.Data, image32.NearInfrared.Data, noDataSaturatingFromAbove: true);
            }
            DataTypeExtensions.Pack(this.Intensity.Data, image32.Intensity.Data, noDataSaturatingFromAbove: true);

            DataTypeExtensions.Pack(this.IntensitySecondReturn.Data, image32.IntensitySecondReturn.Data, noDataSaturatingFromAbove: true);
            DataTypeExtensions.Pack(this.FirstReturns.Data, image32.FirstReturns.Data, noDataSaturatingFromAbove: false);
            DataTypeExtensions.Pack(this.SecondReturns.Data, image32.SecondReturns.Data, noDataSaturatingFromAbove: false);

            return image32;
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if ((name == null) || String.Equals(name, this.Red.Name, StringComparison.Ordinal))
            {
                band = this.Red;
            }
            else if (String.Equals(name, this.Green.Name, StringComparison.Ordinal))
            {
                band = this.Green;
            }
            else if (String.Equals(name, this.Blue.Name, StringComparison.Ordinal))
            {
                band = this.Blue;
            }
            else if ((this.NearInfrared != null) && String.Equals(name, this.NearInfrared.Name, StringComparison.Ordinal))
            {
                band = this.NearInfrared;
            }
            else if (String.Equals(name, this.Intensity.Name, StringComparison.Ordinal))
            {
                band = this.Intensity;
            }
            else if (String.Equals(name, this.IntensitySecondReturn.Name, StringComparison.Ordinal))
            {
                band = this.IntensitySecondReturn;
            }
            else if (String.Equals(name, this.FirstReturns.Name, StringComparison.Ordinal))
            {
                band = this.FirstReturns;
            }
            else if (String.Equals(name, this.SecondReturns.Name, StringComparison.Ordinal))
            {
                band = this.SecondReturns;
            }
            else
            {
                band = null;
                return false;
            }

            return true;
        }

        public override void Write(string imagePath, bool compress)
        {
            int nearInfraredOffset = this.NearInfrared != null ? 1 : 0;
            using Dataset dsmDataset = this.CreateGdalRasterAndSetFilePath(imagePath, bands: 7 + nearInfraredOffset, RasterBand.GetGdalDataType<TPixel>(), compress);
            this.Red.Write(dsmDataset, 1);
            this.Green.Write(dsmDataset, 2);
            this.Blue.Write(dsmDataset, 3);
            if (nearInfraredOffset > 0) 
            {
                Debug.Assert(this.NearInfrared != null);
                this.NearInfrared.Write(dsmDataset, 4);
            }
            this.Intensity.Write(dsmDataset, 4 + nearInfraredOffset);

            this.IntensitySecondReturn.Write(dsmDataset, 5 + nearInfraredOffset);
            this.FirstReturns.Write(dsmDataset, 6 + nearInfraredOffset);
            this.SecondReturns.Write(dsmDataset, 7 + nearInfraredOffset);

            this.FilePath = imagePath;
        }
    }
}