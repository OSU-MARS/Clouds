using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    public class RasterBand<TBand> : Grid where TBand : INumber<TBand>
    {
        private bool noDataIsNaN;

        public Memory<TBand> Data { get; private init; }
        public bool HasNoDataValue { get; private set; }
        public string Name { get; set; }
        public TBand NoDataValue { get; private set; }

        public RasterBand(Band band, SpatialReference crs, GridGeoTransform transform, Memory<TBand> data)
            : this(band.GetDescription(), crs, transform, band.XSize, band.YSize, data)
        {
            band.GetNoDataValue(out double noDataValue, out int hasNoDataValue);
            
            this.HasNoDataValue = hasNoDataValue != 0;
            if (this.HasNoDataValue)
            {                
                this.NoDataValue = TBand.CreateChecked(noDataValue);
            }
            else
            {
                this.NoDataValue = Raster<TBand>.GetDefaultNoDataValue();
            }
            this.noDataIsNaN = TBand.IsNaN(this.NoDataValue);
        }

        public RasterBand(string name, SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, Memory<TBand> data)
            : base(crs, transform, xSize, ySize)
        {
            this.HasNoDataValue = false;
            this.noDataIsNaN = false;
            this.NoDataValue = Raster<TBand>.GetDefaultNoDataValue();

            this.Data = data;
            this.Name = name;
        }

        public TBand this[int index]
        {
            get { return this.Data.Span[index]; }
            set { this.Data.Span[index] = value; }
        }

        public TBand this[int xIndex, int yIndex]
        {
            get { return this[this.ToCellIndex(xIndex, yIndex)]; }
            set { this[this.ToCellIndex(xIndex, yIndex)] = value; }
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
