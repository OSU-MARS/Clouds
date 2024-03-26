using Mars.Clouds.Extensions;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
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
            Driver rasterDriver = GdalExtensions.GetDriverByExtension(rasterPath);
            if (File.Exists(rasterPath))
            {
                // no overwrite option in GTiff.Create(), likely also the case for other drivers
                // This will throw ApplicationException if the raster file is incomplete.
                rasterDriver.Delete(rasterPath);
            }

            string[] rasterDriverOptions = compress ? Raster.DefaultGeoTiffCompressionOptions : [];
            Dataset rasterDataset = rasterDriver.Create(rasterPath, this.SizeX, this.SizeY, bands, cellDataType, rasterDriverOptions);
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

        public static (DataType dataType, long totalCells) GetBandProperties(Dataset rasterDataset)
        {
            if (rasterDataset.RasterCount != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset));
            }

            Band band = rasterDataset.GetRasterBand(1);
            long totalCells = (long)band.XSize * (long)band.YSize;
            return (band.DataType, totalCells);
        }

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

        public static Raster Read(Dataset rasterDataset)
        {
            if (rasterDataset.RasterCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset), "Dataset contains no raster bands.");
            }
            Band band1 = rasterDataset.GetRasterBand(1);
            return band1.DataType switch
            {
                DataType.GDT_Byte => new Raster<byte>(rasterDataset),
                DataType.GDT_Float32 => new Raster<float>(rasterDataset),
                DataType.GDT_Float64 => new Raster<double>(rasterDataset),
                DataType.GDT_Int8 => new Raster<sbyte>(rasterDataset),
                DataType.GDT_Int16 => new Raster<Int16>(rasterDataset),
                DataType.GDT_Int32 => new Raster<Int32>(rasterDataset),
                DataType.GDT_Int64 => new Raster<Int64>(rasterDataset),
                DataType.GDT_UInt16 => new Raster<UInt16>(rasterDataset),
                DataType.GDT_UInt32 => new Raster<UInt32>(rasterDataset),
                DataType.GDT_UInt64 => new Raster<UInt64>(rasterDataset),
                DataType.GDT_Unknown => throw new NotSupportedException("Raster data type is unknown (" + band1.DataType + ")."),
                DataType.GDT_CFloat32 or
                DataType.GDT_CFloat64 or
                DataType.GDT_CInt16 or
                DataType.GDT_CInt32 or
                _ => throw new NotSupportedException("Unhandled raster data type " + band1.DataType + ".")
            };
        }

        public abstract bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band);

        public abstract void Write(string rasterPath, bool compress);

        protected void WriteBand(Dataset rasterDataset, RasterBand band, int gdalBandIndex)
        {
            Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
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
    public class Raster<TBand> : Raster, IFileSerializable<Raster<TBand>> where TBand : INumber<TBand>
    {
        public RasterBand<TBand>[] Bands { get; private init; }

        public Raster(Dataset rasterDataset)
            : base(rasterDataset)
        {
            if (rasterDataset.RasterCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset), "Raster has no bands.");
            }
            string[] sourceFiles = rasterDataset.GetFileList();
            Debug.Assert(sourceFiles.Length > 0);
            this.FilePath = sourceFiles[0]; // for now, assume primary source file is always the first file in the raster's sources

            // check bands for consistency
            for (int gdalBandIndex = 1; gdalBandIndex <= rasterDataset.RasterCount; ++gdalBandIndex)
            {
                Band band = rasterDataset.GetRasterBand(gdalBandIndex);
                if (this.SizeX != band.XSize)
                {
                    if (this.SizeX == -1)
                    {
                        this.SizeX = band.XSize;
                    }
                    else
                    {
                        throw new NotSupportedException("Previous bands are " + this.SizeX + " by " + this.SizeY + " cells but band " + gdalBandIndex + " is " + band.XSize + " by " + band.YSize + " cells.");
                    }
                }
                if (this.SizeY != band.YSize)
                {
                    if (this.SizeY == -1)
                    {
                        this.SizeY = band.YSize;
                    }
                    else
                    {
                        throw new NotSupportedException("Previous bands are " + this.SizeX + " by " + this.SizeY + " cells but band " + gdalBandIndex + " is " + band.XSize + " by " + band.YSize + " cells.");
                    }
                }
            }

            // allocate data and create bands
            DataType gdalDataType = RasterBand.GetGdalDataType<TBand>();

            this.Bands = new RasterBand<TBand>[rasterDataset.RasterCount];
            int[] bandMap = ArrayExtensions.CreateSequence(1, this.Bands.Length);
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                int gdalBandIndex = bandIndex + 1;
                Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
                if (gdalBand.DataType != gdalDataType)
                {
                    throw new NotSupportedException("A Raster<" + typeof(TBand).Name + "> cannot be loaded from '" + this.FilePath + "' because band " + gdalBandIndex + " is of type " + gdalBand.DataType + ".");
                }

                RasterBand<TBand> band = new(gdalBand, this);
                this.Bands[bandIndex] = band;

                // for now, load all raster data on creation
                // In some use cases or failure paths it's desirable to load the band lazily but this isn't currently supported.
                // See https://stackoverflow.com/questions/15958830/c-sharp-generics-cast-generic-type-to-value-type for discussion of
                // C# generics' limitations in recognizing TBand[] as float[], int[], ...
                GCHandle dataPin = GCHandle.Alloc(band.Data, GCHandleType.Pinned);
                try
                {
                    CPLErr gdalErrorCode = rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.SizeX, ySize: this.SizeY, buffer: dataPin.AddrOfPinnedObject(), buf_xSize: this.SizeX, buf_ySize: this.SizeY, buf_type: gdalBand.DataType, bandCount: this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    GdalException.ThrowIfError(gdalErrorCode, nameof(rasterDataset.ReadRaster));
                }
                finally
                {
                    dataPin.Free();
                }
            }
        }

        public Raster(Grid extent, string[] bandNames, TBand noDataValue)
            : this(extent.Crs, extent.Transform, extent.SizeX, extent.SizeY, bandNames, noDataValue)
        {
        }

        public Raster(Grid extent, string[] bandNames)
            : this(extent.Crs, extent.Transform, extent.SizeX, extent.SizeY, bandNames)
        {
        }

        public Raster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, string[] bandNames, TBand noDataValue)
            : this(crs, transform, xSize, ySize, bandNames)
        {
            this.SetNoDataOnAllBands(noDataValue);
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                Array.Fill(this.Bands[bandIndex].Data, noDataValue);
            }
        }

        public Raster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, string[] bandNames)
            : base(crs, transform, xSize, ySize)
        {
            if (bandNames.Length < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bandNames), "Rasters must have at least one band but " + bandNames.Length + " band names were specified.");
            }

            this.Bands = new RasterBand<TBand>[bandNames.Length];
            for (int bandIndex = 0; bandIndex < bandNames.Length; ++bandIndex)
            {
                this.Bands[bandIndex] = new(bandNames[bandIndex], this);
            }
        }

        public new RasterBand<TBand> GetBand(string? name)
        {
            if (this.TryGetBand(name, out RasterBand<TBand>? band) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
            }

            return band;
        }

        public static Raster<TBand> Read(string rasterPath)
        {
            using Dataset rasterDataset = Gdal.Open(rasterPath, Access.GA_ReadOnly);
            (DataType dataType, long totalCells) = Raster.GetBandProperties(rasterDataset);

            DataType expectedDataType = RasterBand.GetGdalDataType<TBand>();
            if (dataType != expectedDataType)
            {
                throw new NotSupportedException("Raster '" + rasterPath + "' has data type " + dataType + " instead of " + expectedDataType + ".");
            }
            if (totalCells > Array.MaxLength)
            {
                throw new NotSupportedException("Raster '" + rasterPath + "' has " + totalCells + " cells, which exceeds the maximum supported size of " + Array.MaxLength + " cells.");
            }

            return new Raster<TBand>(rasterDataset)
            {
                FilePath = rasterPath
            };
        }

        protected void SetNoDataOnAllBands(TBand noDataValue)
        {
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                this.Bands[bandIndex].SetNoDataValue(noDataValue);
            }
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
            if ((name == null) && (this.Bands.Length == 1))
            {
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
            DataType cellDataType = RasterBand.GetGdalDataType<TBand>();

            using Dataset rasterDataset = this.CreateGdalRasterAndSetFilePath(rasterPath, this.Bands.Length, cellDataType, compress);
            for (int bandIndex = 0; bandIndex< this.Bands.Length; ++bandIndex)
            {
                RasterBand<TBand> band = this.Bands[bandIndex];
                int gdalbandIndex = bandIndex + 1;

                this.WriteBand(rasterDataset, band, gdalbandIndex);
            }
        }
    }
}
