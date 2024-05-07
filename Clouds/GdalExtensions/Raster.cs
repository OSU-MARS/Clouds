using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Mars.Clouds.GdalExtensions
{
    public abstract class Raster : Grid
    {
        protected static readonly string[] DefaultGeoTiffCompressionOptions;

        public string FilePath { get; set; }

        static Raster()
        {
            Raster.DefaultGeoTiffCompressionOptions = [ "COMPRESS=DEFLATE", "PREDICTOR=2", "ZLEVEL=9" ];
        }

        protected Raster(Dataset rasterDataset)
            : this(rasterDataset.GetSpatialRef(), new(rasterDataset), -1, -1)
        {
            this.FilePath = rasterDataset.GetFirstFile(); // for now, assume primary source file is always the first file in the raster's sources
        }

        protected Raster(Grid extent)
            : this(extent.Crs, extent.Transform, extent.SizeX, extent.SizeY)
        {
        }

        protected Raster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize)
            : base(crs, transform, xSize, ySize, cloneCrsAndTransform: true)
        {
            this.FilePath = String.Empty;
        }

        protected Dataset CreateGdalRasterAndSetFilePath(string rasterPath, int bands, DataType cellDataType, bool compress)
        {
            using Driver rasterDriver = GdalExtensions.GetDriverByExtension(rasterPath);
            if (File.Exists(rasterPath))
            {
                // no overwrite option in GTiff.Create(), likely also the case for other drivers
                // This will throw ApplicationException with 0x80131600 = -2146232832 if the raster file is incomplete.
                try
                {
                    rasterDriver.Delete(rasterPath);
                }
                catch (ApplicationException gdalDeletionError)
                {
                    if (gdalDeletionError.HResult != -2146232832)
                    {
                        throw;
                    }

                    File.Delete(rasterPath);
                }
            }

            string[] rasterDriverOptions = compress ? Raster.DefaultGeoTiffCompressionOptions : [];
            Dataset rasterDataset = rasterDriver.Create(rasterPath, this.SizeX, this.SizeY, bands, cellDataType, rasterDriverOptions); // caller is responsible for disposal
            rasterDataset.SetGeoTransform(this.Transform.GetPadfTransform());
            rasterDataset.SetSpatialRef(this.Crs);

            this.FilePath = rasterPath;
            return rasterDataset;
        }

        public RasterBand GetBand(string? name)
        {
            if (this.TryGetBand(name, out RasterBand? band) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
            }

            return band;
        }

        public abstract int GetBandIndex(string name);

        public abstract IEnumerable<RasterBand> GetBands();

        // eight-way immediate adjacency
        public static bool IsNeighbor8(int rowOffset, int columnOffset)
        {
            // exclude all cells with Euclidean grid distance >= 2.0
            int absRowOffset = Math.Abs(rowOffset);
            if (absRowOffset > 1)
            {
                return false;
            }

            int absColumnOffset = Math.Abs(columnOffset);
            if (absColumnOffset > 1)
            {
                return false;
            }

            // remaining nine possibilities have 0.0 <= Euclidean grid distance <= sqrt(2.0) and 0 <= Manhattan distance <= 2
            // Of these, only the self case (row offset = column offset = 0) needs to be excluded.
            return (absRowOffset + absColumnOffset) > 0;
        }

        public static Raster Read(Dataset rasterDataset, bool readData)
        {
            if (rasterDataset.RasterCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset), "Dataset contains no raster bands.");
            }
            using Band gdalBand1 = rasterDataset.GetRasterBand(1);
            Raster raster = gdalBand1.DataType switch
            {
                DataType.GDT_Byte => new Raster<byte>(rasterDataset, readData),
                DataType.GDT_Float32 => new Raster<float>(rasterDataset, readData),
                DataType.GDT_Float64 => new Raster<double>(rasterDataset, readData),
                DataType.GDT_Int8 => new Raster<sbyte>(rasterDataset, readData),
                DataType.GDT_Int16 => new Raster<Int16>(rasterDataset, readData),
                DataType.GDT_Int32 => new Raster<Int32>(rasterDataset, readData),
                DataType.GDT_Int64 => new Raster<Int64>(rasterDataset, readData),
                DataType.GDT_UInt16 => new Raster<UInt16>(rasterDataset, readData),
                DataType.GDT_UInt32 => new Raster<UInt32>(rasterDataset, readData),
                DataType.GDT_UInt64 => new Raster<UInt64>(rasterDataset, readData),
                DataType.GDT_Unknown => throw new NotSupportedException("Raster data type is unknown (" + gdalBand1.DataType + ")."),
                //DataType.GDT_CFloat32 or
                //DataType.GDT_CFloat64 or
                //DataType.GDT_CInt16 or
                //DataType.GDT_CInt32 or
                _ => throw new NotSupportedException("Unhandled raster data type " + gdalBand1.DataType + ".")
            };
            return raster;
        }

        public abstract bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band);

        public abstract void Write(string rasterPath, bool compress);

        protected void WriteBand(Dataset rasterDataset, RasterBand band, int gdalBandIndex)
        {
            using Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
            gdalBand.SetDescription(band.Name);
            if (band.HasNoDataValue)
            {
                gdalBand.SetNoDataValue(RasterBand.GetDefaultNoDataValueAsDouble(gdalBand.DataType));
            }

            GCHandle dataPin = gdalBand.DataType switch
            {
                DataType.GDT_Byte => band.GetPinnedDataHandle<byte>(RasterBand<byte>.GetDefaultNoDataValue()),
                DataType.GDT_Float32 => band.GetPinnedDataHandle<float>(RasterBand<float>.GetDefaultNoDataValue()),
                DataType.GDT_Float64 => band.GetPinnedDataHandle<double>(RasterBand<double>.GetDefaultNoDataValue()),
                DataType.GDT_Int8 => band.GetPinnedDataHandle<sbyte>(RasterBand<sbyte>.GetDefaultNoDataValue()),
                DataType.GDT_Int16 => band.GetPinnedDataHandle<Int16>(RasterBand<Int16>.GetDefaultNoDataValue()),
                DataType.GDT_Int32 => band.GetPinnedDataHandle<Int32>(RasterBand<Int32>.GetDefaultNoDataValue()),
                DataType.GDT_Int64 => band.GetPinnedDataHandle<Int64>(RasterBand<Int64>.GetDefaultNoDataValue()),
                DataType.GDT_UInt16 => band.GetPinnedDataHandle<UInt16>(RasterBand<UInt16>.GetDefaultNoDataValue()),
                DataType.GDT_UInt32 => band.GetPinnedDataHandle<UInt32>(RasterBand<UInt32>.GetDefaultNoDataValue()),
                DataType.GDT_UInt64 => band.GetPinnedDataHandle<UInt64>(RasterBand<UInt64>.GetDefaultNoDataValue()),
                _ => throw new ArgumentOutOfRangeException(nameof(rasterDataset), "Unhandled output data type " + gdalBand.DataType + ".")
            };
            try
            {
                CPLErr gdalErrorCode = gdalBand.WriteRaster(xOff: 0, yOff: 0, xSize: this.SizeX, ySize: this.SizeY, buffer: dataPin.AddrOfPinnedObject(), buf_xSize: this.SizeX, buf_ySize: this.SizeY, buf_type: gdalBand.DataType, pixelSpace: 0, lineSpace: 0);
                GdalException.ThrowIfError(gdalErrorCode, nameof(rasterDataset.WriteRaster));
            }
            finally
            {
                dataPin.Free();
            }
        }
    }

    /// <summary>
    /// A default raster implementation where all bands are of the same type.
    /// </summary>
    public class Raster<TBand> : Raster, IRasterSerializable<Raster<TBand>> where TBand : IMinMaxValue<TBand>, INumber<TBand>
    {
        public RasterBand<TBand>[] Bands { get; private init; }

        public Raster(Dataset rasterDataset, bool loadData)
            : base(rasterDataset)
        {
            if (rasterDataset.RasterCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset), "Raster has no bands.");
            }

            // allocate data and create bands
            // Also check bands for consistency.
            this.Bands = new RasterBand<TBand>[rasterDataset.RasterCount];
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                int gdalBandIndex = bandIndex + 1;
                using Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
                if (this.SizeX == -1)
                {
                    this.SizeX = gdalBand.XSize;
                }
                else if (this.SizeX != gdalBand.XSize)
                {
                    throw new NotSupportedException("Previous bands are " + this.SizeX + " by " + this.SizeY + " cells but band " + gdalBandIndex + " is " + gdalBand.XSize + " by " + gdalBand.YSize + " cells.");
                }
                if (this.SizeY == -1)
                {
                    this.SizeY = gdalBand.YSize;
                }
                else if (this.SizeY != gdalBand.YSize)
                {
                    throw new NotSupportedException("Previous bands are " + this.SizeX + " by " + this.SizeY + " cells but band " + gdalBandIndex + " is " + gdalBand.XSize + " by " + gdalBand.YSize + " cells.");
                }

                this.Bands[bandIndex] = new(rasterDataset, gdalBand, loadData);
            }
        }

        public Raster(Grid extent, string[] bandNames, TBand noDataValue)
            : this(extent.Crs, extent.Transform, extent.SizeX, extent.SizeY, bandNames, noDataValue, true)
        {
        }

        protected Raster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, string[] bandNames, TBand noDataValue, bool initializeBandsToNoData)
            : base(crs, transform, xSize, ySize)
        {
            if (bandNames.Length < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bandNames), "Rasters must have at least one band but " + bandNames.Length + " band names were specified.");
            }

            this.Bands = new RasterBand<TBand>[bandNames.Length];
            for (int bandIndex = 0; bandIndex < bandNames.Length; ++bandIndex)
            {
                this.Bands[bandIndex] = new(this, bandNames[bandIndex], noDataValue, initializeBandsToNoData);
            }
        }

        public override int GetBandIndex(string name)
        {
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                RasterBand<TBand> candidateBand = this.Bands[bandIndex];
                if (String.Equals(candidateBand.Name, name, StringComparison.Ordinal))
                {
                    return bandIndex;
                }
            }

            throw new NotSupportedException("Raster does not contain a band named '" + name + "'.");
        }

        public override IEnumerable<RasterBand<TBand>> GetBands()
        {
            return this.Bands;
        }

        public static Raster<TBand> Read(string rasterPath, bool readData)
        {
            using Dataset rasterDataset = Gdal.Open(rasterPath, Access.GA_ReadOnly);
            Raster<TBand> raster = new(rasterDataset, readData);
            Debug.Assert(String.Equals(rasterPath, rasterPath, StringComparison.OrdinalIgnoreCase));
            return raster;
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if (this.TryGetBand(name, out RasterBand<TBand>? typedBand))
            {
                band = typedBand;
                return true;
            }

            band = null;
            return false;
        }

        public bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand<TBand>? band)
        {
            if (name == null)
            {
                Debug.Assert(this.Bands.Length > 0);
                band = this.Bands[0];
                return true;
            }

            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                RasterBand<TBand> candidateBand = this.Bands[bandIndex];
                if (String.Equals(candidateBand.Name, name, StringComparison.Ordinal))
                {
                    band = candidateBand;
                    return true;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
        }

        //public bool TryGetNoDataValue(out TBand noDataValue)
        //{
        //    int bandsWithNoData = 0;
        //    noDataValue = TBand.Zero;
        //    for (int bandIndex = 0; bandIndex < this.BandCount; ++bandIndex)
        //    {
        //        RasterBand<TBand> band = this.Bands[bandIndex];
        //        if (band.HasNoDataValue)
        //        {
        //            if (bandsWithNoData == 0)
        //            {
        //                noDataValue = band.NoDataValue;
        //            }
        //            else if (band.IsNoData(noDataValue) == false)
        //            {
        //                throw new NotSupportedException("Raster bands have different no data values. At least " + band.NoDataValue + " on band " + band.Name + "(band " + (bandIndex + 1) + ") and " + noDataValue + " on a lower numbered band.");
        //            }

        //            ++bandsWithNoData;
        //        }
        //    }

        //    if ((bandsWithNoData != 0) && (bandsWithNoData != this.BandCount))
        //    {
        //        throw new NotSupportedException("Raster has " + bandsWithNoData + " bands with no data values and " + (this.BandCount - bandsWithNoData) + " bands without no data values. Whether or not there is a raster level no data value is therefore not well defined.");
        //    }
        //    return bandsWithNoData > 0;
        //}

        public override void Write(string rasterPath, bool compress)
        {
            // all bands have the same type, so no need for type conversion to meet GDAL (and GeoTIFF) single type constraints
            DataType gdalDataType = RasterBand.GetGdalDataType<TBand>();
            using Dataset rasterDataset = this.CreateGdalRasterAndSetFilePath(rasterPath, this.Bands.Length, gdalDataType, compress);
            for (int bandIndex = 0; bandIndex< this.Bands.Length; ++bandIndex)
            {
                RasterBand<TBand> band = this.Bands[bandIndex];
                int gdalbandIndex = bandIndex + 1;

                this.WriteBand(rasterDataset, band, gdalbandIndex);
            }
        }
    }
}
