using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    public class SinglebandRaster
    {
        public SpatialReference Crs { get; private init; }
        public string FilePath { get; init; }
        public RasterGeoTransform Transform { get; private init; }
        public int XSize { get; protected init; }
        public int YSize { get; protected init; }

        public SinglebandRaster(Dataset rasterDataset)
        {
            this.Crs = rasterDataset.GetSpatialRef();
            this.FilePath = String.Empty;

            this.Transform = new(rasterDataset);
            this.XSize = -1;
            this.YSize = -1;
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

    public class SinglebandRaster<TBand> : SinglebandRaster where TBand : INumber<TBand>
    {
        private readonly bool hasNoDataValue;
        private readonly bool noDataIsNaN;
        private readonly TBand noDataValue;

        public TBand[] Data { get; private init; }

        public SinglebandRaster(Dataset rasterDataset)
            : base(rasterDataset)
        {
            if (rasterDataset.RasterCount != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset));
            }

            // for now, load raster data on creation
            // In some use cases or failure paths it's desirable to load the band lazily but this isn't currently supported.
            Band band = rasterDataset.GetRasterBand(1);
            this.Data = new TBand[band.XSize * band.YSize];
            band.GetNoDataValue(out double noDataValue, out int hasNoDataValue);
            this.hasNoDataValue = hasNoDataValue != 0;

            if (Type.GetTypeCode(typeof(TBand)) == TypeCode.Single)
            {
                band.ReadRaster(0, 0, band.XSize, band.YSize, (float[])(object)this.Data, band.XSize, band.YSize, 0, 0);
                this.noDataValue = this.hasNoDataValue ? (TBand)(object)Convert.ToSingle(noDataValue) : (TBand)(object)Single.NaN;
                this.noDataIsNaN = this.hasNoDataValue && TBand.IsNaN(this.noDataValue);
            }
            else
            {
                throw new NotSupportedException("Unhandled data type " + Type.GetTypeCode(typeof(TBand)) + ".");
            }

            this.XSize = band.XSize;
            this.YSize = band.YSize;
        }

        public TBand this[int rowIndex, int columnIndex]
        {
            get { return this.Data[columnIndex + rowIndex * this.YSize]; }
        }

        public bool IsNoData(TBand value)
        {
            if (this.hasNoDataValue)
            {
                return this.noDataIsNaN ? TBand.IsNaN(value) : this.noDataValue == value; // have to test with IsNaN() since float.NaN == float.NaN = false
            }
            return false;
        }
    }
}
