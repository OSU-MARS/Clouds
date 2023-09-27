using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    public class RasterBand<TBand> where TBand : INumber<TBand>
    {
        private bool noDataIsNaN;

        public SpatialReference Crs { get; private init; }
        public Memory<TBand> Data { get; private init; }

        public bool HasNoDataValue { get; private set; }
        public string Name { get; set; }
        public TBand NoDataValue { get; private set; }

        public RasterGeoTransform Transform { get; private init; }
        public int XSize { get; private init; }
        public int YSize { get; private init; }

        public RasterBand(Band band, SpatialReference crs, RasterGeoTransform transform, Memory<TBand> data)
            : this(band.GetDescription(), crs, transform, band.XSize, band.YSize, data)
        {
            band.GetNoDataValue(out double noDataValue, out int hasNoDataValue);

            this.HasNoDataValue = hasNoDataValue != 0;
            if (Type.GetTypeCode(typeof(TBand)) == TypeCode.Single)
            {
                this.NoDataValue = this.HasNoDataValue ? (TBand)(object)Single.CreateChecked(noDataValue) : (TBand)(object)Single.NaN;
                this.noDataIsNaN = this.HasNoDataValue && TBand.IsNaN(this.NoDataValue);
            }
            else
            {
                throw new NotSupportedException("Unhandled data type " + Type.GetTypeCode(typeof(TBand)) + ".");
            }
        }

        public RasterBand(string name, SpatialReference crs, RasterGeoTransform transform, int xSize, int ySize, Memory<TBand> data)
        {
            this.HasNoDataValue = false;
            this.noDataIsNaN = false;
            if (Type.GetTypeCode(typeof(TBand)) == TypeCode.Single)
            {
                this.NoDataValue = (TBand)(object)Single.NaN;
            }
            else
            {
                throw new NotSupportedException("Unhandled data type " + Type.GetTypeCode(typeof(TBand)) + ".");
            }

            this.Crs = crs;
            this.Data = data;
            this.Name = name;
            this.Transform = transform;
            this.XSize = xSize;
            this.YSize = ySize;
        }

        public TBand this[int rowIndex, int columnIndex]
        {
            get { return this.Data.Span[columnIndex + rowIndex * this.YSize]; }
            set { this.Data.Span[columnIndex + rowIndex * this.YSize] = value; }
        }

        public bool IsNoData(TBand value)
        {
            if (this.HasNoDataValue)
            {
                return this.noDataIsNaN ? TBand.IsNaN(value) : this.NoDataValue == value; // have to test with IsNaN() since float.NaN == float.NaN = false
            }
            return false;
        }

        public void SetNoDataValue(TBand noDataValue)
        {
            this.HasNoDataValue = true;
            this.noDataIsNaN = TBand.IsNaN(this.NoDataValue);
            this.NoDataValue = noDataValue;
        }
    }
}
