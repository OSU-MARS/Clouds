using Mars.Clouds.Extensions;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.GdalExtensions
{
    public abstract class Raster : Grid
    {
        protected static readonly string[] DefaultCompressionOptions;

        protected DataType CellDataType { get; private init; }

        public abstract int BandCount { get; }
        public string FilePath { get; set; }

        static Raster()
        {
            Raster.DefaultCompressionOptions = [ "COMPRESS=DEFLATE", "PREDICTOR=2", "ZLEVEL=9" ];
        }

        protected Raster(Dataset rasterDataset, DataType cellDataType)
            : this(rasterDataset.GetSpatialRef(), new(rasterDataset), -1, -1, cellDataType)
        {
        }

        protected Raster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, DataType cellDataType)
            : base(crs, transform, xSize, ySize)
        {
            this.CellDataType = cellDataType;
            this.FilePath = String.Empty;
        }

        public static Raster Create(Dataset rasterDataset)
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
                DataType.GDT_UInt16 => new Raster<Int64>(rasterDataset),
                DataType.GDT_UInt32 => new Raster<Int64>(rasterDataset),
                DataType.GDT_UInt64 => new Raster<Int64>(rasterDataset),
                DataType.GDT_Unknown => throw new NotSupportedException("Raster data type is unknown (" + band1.DataType + ")."),
                DataType.GDT_CFloat32 or
                DataType.GDT_CFloat64 or
                DataType.GDT_CInt16 or
                DataType.GDT_CInt32 or
                _ => throw new NotSupportedException("Unhandled raster data type " + band1.DataType + ".")
            };
        }

        public abstract RasterBand GetBand(int bandIndex);

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
    }

    public class Raster<TBand> : Raster where TBand : INumber<TBand>
    {
        public RasterBand<TBand>[] Bands { get; private init; }
        public TBand[] Data { get; private init; } // GDAL type layout: all of band 1, all of band 2, ...

        public Raster(Dataset rasterDataset)
            : base(rasterDataset, Raster<TBand>.GetGdalDataType())
        {
            if (rasterDataset.RasterCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset), "Raster has no bands.");
            }

            // check bands for consistency
            for (int gdalBandIndex = 1; gdalBandIndex <= rasterDataset.RasterCount; ++gdalBandIndex)
            {
                Band band = rasterDataset.GetRasterBand(gdalBandIndex);
                if (this.XSize != band.XSize)
                {
                    if (this.XSize == -1)
                    {
                        this.XSize = band.XSize;
                    }
                    else
                    {
                        throw new NotSupportedException("Previous bands are " + this.XSize + " by " + this.YSize + " cells but band " + gdalBandIndex + " is " + band.XSize + " by " + band.YSize + " cells.");
                    }
                }
                if (this.YSize != band.YSize)
                {
                    if (this.YSize == -1)
                    {
                        this.YSize = band.YSize;
                    }
                    else
                    {
                        throw new NotSupportedException("Previous bands are " + this.XSize + " by " + this.YSize + " cells but band " + gdalBandIndex + " is " + band.XSize + " by " + band.YSize + " cells.");
                    }
                }
            }

            // allocate data and create bands
            this.Bands = new RasterBand<TBand>[rasterDataset.RasterCount];
            this.Data = new TBand[rasterDataset.RasterCount * this.XSize * this.YSize];

            int cellsPerBand = this.XSize * this.YSize;
            for (int bandOffset = 0, bandIndex = 0; bandIndex < this.Bands.Length; bandOffset += cellsPerBand, ++bandIndex)
            {
                int gdalBandIndex = bandIndex + 1;
                RasterBand<TBand> band = new(rasterDataset.GetRasterBand(gdalBandIndex), this.Crs, this.Transform, new(this.Data, bandOffset, cellsPerBand));
                this.Bands[bandIndex] = band;
            }

            // for now, load raster data on creation
            // In some use cases or failure paths it's desirable to load the band lazily but this isn't currently supported.
            // See https://stackoverflow.com/questions/15958830/c-sharp-generics-cast-generic-type-to-value-type for discussion of
            // C# generics' limitations in recognizing TBand[] as float[], int[], ...
            int[] bandMap = ArrayExtensions.CreateSequence(1, this.Bands.Length);
            switch (this.CellDataType)
            {
                case DataType.GDT_Byte:
                    rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<byte[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                case DataType.GDT_Float32:
                    rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<float[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                case DataType.GDT_Float64:
                    rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<double[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                case DataType.GDT_Int16:
                    rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<Int16[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                case DataType.GDT_Int32:
                    rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<Int32[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                // no ReadRaster() overloads for Int8, Int64, UInt16, UInt32, UInt64 as of MaxRev.Gdal 3.7.0: use closest type of same size instead
                // https://github.com/MaxRev-Dev/gdal.netcore/issues/111
                case DataType.GDT_Int8:
                    rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<byte[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                case DataType.GDT_Int64:
                    rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<double[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                case DataType.GDT_UInt16:
                    rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<Int16[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                case DataType.GDT_UInt32:
                    rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<Int32[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                case DataType.GDT_UInt64:
                    rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<double[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                default:
                    throw new NotSupportedException("Unhandled cell data type " + this.CellDataType + ".");
            }
        }

        public Raster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, int bands, TBand noDataValue)
            : this(crs, transform, xSize, ySize, bands)
        {
            Array.Fill(this.Data, noDataValue);

            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                this.Bands[bandIndex].SetNoDataValue(noDataValue);
            }
        }

        public Raster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, int bands)
            : base(crs, transform, xSize, ySize, Raster<TBand>.GetGdalDataType())
        {
            this.Bands = new RasterBand<TBand>[bands];
            this.Data = new TBand[xSize * ySize * bands];

            int cellsPerBand = this.XSize * this.YSize;
            for (int bandOffset = 0, bandIndex = 0; bandIndex < this.Bands.Length; bandOffset += cellsPerBand, ++bandIndex)
            {
                this.Bands[bandIndex] = new(name: String.Empty, this.Crs, this.Transform, this.XSize, this.YSize, new(this.Data, bandOffset, cellsPerBand));
            }
        }

        public override int BandCount 
        { 
            get { return this.Bands.Length; } 
        }

        public override RasterBand GetBand(int bandIndex)
        {
            return this.Bands[bandIndex];
        }

        public static TBand GetDefaultNoDataValue()
        {
            return Type.GetTypeCode(typeof(TBand)) switch
            {
                TypeCode.Byte => TBand.CreateChecked(Byte.MaxValue),
                TypeCode.Double => TBand.CreateChecked(Double.NaN),
                TypeCode.Int16 => TBand.CreateChecked(Int16.MinValue),
                TypeCode.Int32 => TBand.CreateChecked(Int32.MinValue),
                TypeCode.Int64 => TBand.CreateChecked(Int64.MinValue),
                TypeCode.SByte => TBand.CreateChecked(SByte.MinValue),
                TypeCode.Single => TBand.CreateChecked(Single.NaN),
                TypeCode.UInt16 => TBand.CreateChecked(UInt16.MaxValue),
                TypeCode.UInt32 => TBand.CreateChecked(UInt32.MaxValue),
                TypeCode.UInt64 => TBand.CreateChecked(UInt64.MaxValue),
                // complex numbers (GDT_CInt16, 32, CFloat32, 64) and not GDT_TypeCount not currently reachable
                _ => throw new NotSupportedException("Unhandled data type " + Type.GetTypeCode(typeof(TBand)) + ".")
            };
        }

        public static DataType GetGdalDataType()
        {
            return Type.GetTypeCode(typeof(TBand)) switch
            {
                TypeCode.Byte => DataType.GDT_Byte,
                TypeCode.Double => DataType.GDT_Float64,
                TypeCode.Int16 => DataType.GDT_Int16,
                TypeCode.Int32 => DataType.GDT_Int32,
                TypeCode.Int64 => DataType.GDT_Int64,
                TypeCode.SByte => DataType.GDT_Int8,
                TypeCode.Single => DataType.GDT_Float32,
                TypeCode.UInt16 => DataType.GDT_UInt16,
                TypeCode.UInt32 => DataType.GDT_UInt32,
                TypeCode.UInt64 => DataType.GDT_UInt64,
                // complex numbers (GDT_CInt16, 32, CFloat32, 64) and not GDT_TypeCount not currently reachable
                _ => throw new NotSupportedException("Unhandled data type " + Type.GetTypeCode(typeof(TBand)) + ".")
            };
        }

        public void Write(string rasterPath)
        {
            Driver rasterDriver = GdalExtensions.GetDriverByExtension(rasterPath);
            if (File.Exists(rasterPath))
            {
                // no overwrite option in GTiff.Create(), likely also the case for other drivers
                // This will throw ApplicationException if the raster file is incomplete.
                rasterDriver.Delete(rasterPath);
            }
            using Dataset rasterDataset = rasterDriver.Create(rasterPath, this.XSize, this.YSize, this.Bands.Length, this.CellDataType, Raster.DefaultCompressionOptions);
            rasterDataset.SetGeoTransform(this.Transform.GetPadfTransform());
            rasterDataset.SetSpatialRef(this.Crs);

            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                RasterBand<TBand> band = this.Bands[bandIndex];
                if (band.HasNoDataValue)
                {
                    int gdalbandIndex = bandIndex + 1;
                    Band gdalBand = rasterDataset.GetRasterBand(gdalbandIndex);
                    gdalBand.SetDescription(band.Name);
                    gdalBand.SetNoDataValue(Double.CreateChecked(band.NoDataValue));
                }
            }

            int[] bandMap = ArrayExtensions.CreateSequence(1, this.Bands.Length);
            switch (this.CellDataType)
            {
                case DataType.GDT_Float32:
                    rasterDataset.WriteRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<float[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, bandCount: this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                default:
                    throw new NotSupportedException("Unhandled cell data type " + this.CellDataType + ".");
            }

            this.FilePath = rasterPath;
        }
    }
}
