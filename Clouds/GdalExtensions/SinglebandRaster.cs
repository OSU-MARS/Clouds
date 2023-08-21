using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    public class SinglebandRaster<TBand> where TBand : INumber<TBand>
    {
        public SpatialReference Crs { get; private init; }
        public TBand[] Data { get; private init; }
        public TBand NoDataValue { get; private init; }
        public RasterGeoTransform Transform { get; private init; }
        public int XSize { get; private init; }
        public int YSize { get; private init; }

        public SinglebandRaster(Dataset rasterDataset)
        {
            if (rasterDataset.RasterCount != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset));
            }

            this.Crs = rasterDataset.GetSpatialRef();

            Band band = rasterDataset.GetRasterBand(1);
            this.Data = new TBand[band.XSize * band.YSize];
            band.GetNoDataValue(out double noDataValue, out int hasNoDataValue);

            if (Type.GetTypeCode(typeof(TBand)) == TypeCode.Single)
            {
                band.ReadRaster(0, 0, band.XSize, band.YSize, (float[])(object)this.Data, band.XSize, band.YSize, 0, 0);
                this.NoDataValue = hasNoDataValue == 1 ? (TBand)(object)Convert.ToSingle(noDataValue) : (TBand)(object)Single.NaN;
            }
            else
            {
                throw new NotSupportedException("Unhandled data type " + Type.GetTypeCode(typeof(TBand)) + ".");
            }

            this.Transform = new(rasterDataset);
            this.XSize = band.XSize;
            this.YSize = band.YSize;
        }

        public TBand this[int rowIndex, int columnIndex]
        {
            get { return this.Data[columnIndex + rowIndex * this.YSize]; }
        }
    }
}
