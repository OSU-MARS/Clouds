using Mars.Clouds.Extensions;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.GdalExtensions
{
    public class Raster
    {
        protected static readonly string[] DefaultCompressionOptions;

        protected DataType CellDataType { get; private init; }

        public SpatialReference Crs { get; private init; }
        public string FilePath { get; set; }
        public RasterGeoTransform Transform { get; private init; }
        public int XSize { get; protected set; }
        public int YSize { get; protected set; }

        static Raster()
        {
            Raster.DefaultCompressionOptions = new string[]
            {
                "COMPRESS=DEFLATE", 
                "PREDICTOR=2", 
                "ZLEVEL=9"
            };
        }

        protected Raster(Dataset rasterDataset, DataType cellDataType)
        {
            this.CellDataType = cellDataType;
            this.Crs = rasterDataset.GetSpatialRef();
            this.FilePath = String.Empty;

            this.Transform = new(rasterDataset);
            this.XSize = -1;
            this.YSize = -1;
        }

        protected Raster(SpatialReference crs, RasterGeoTransform transform, int xSize, int ySize, DataType cellDataType)
        {
            this.CellDataType = cellDataType;
            this.Crs = crs;
            this.FilePath = String.Empty;

            this.Transform = transform;
            this.XSize = xSize;
            this.YSize = ySize;
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
                case DataType.GDT_Float32:
                    rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: this.XSize, ySize: this.YSize, Unsafe.As<float[]>(this.Data), buf_xSize: this.XSize, buf_ySize: this.YSize, this.Bands.Length, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
                    break;
                default:
                    throw new NotSupportedException("Unhandled cell data type " + this.CellDataType + ".");
            }
        }

        public Raster(SpatialReference crs, RasterGeoTransform transform, int xSize, int ySize, int bands, TBand noDataValue)
            : this(crs, transform, xSize, ySize, bands)
        {
            Array.Fill(this.Data, noDataValue);

            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                this.Bands[bandIndex].SetNoDataValue(noDataValue);
            }
        }

        public Raster(SpatialReference crs, RasterGeoTransform transform, int xSize, int ySize, int bands)
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

        private static DataType GetGdalDataType()
        {
            if (Type.GetTypeCode(typeof(TBand)) == TypeCode.Single)
            {
                return DataType.GDT_Float32;
            }
            else
            {
                throw new NotSupportedException("Unhandled data type " + Type.GetTypeCode(typeof(TBand)) + ".");
            }
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
            rasterDataset.SetGeoTransform(this.Transform.PadfTransform);
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
