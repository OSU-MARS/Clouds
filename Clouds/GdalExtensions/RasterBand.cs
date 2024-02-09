using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    public abstract class RasterBand : Grid
    {
        protected bool NoDataIsNaN { get; set; }

        public bool HasNoDataValue { get; protected set; }
        public string Name { get; set; }

        protected RasterBand(string name, SpatialReference crs, GridGeoTransform transform, int xSize, int ySize)
            : base(crs, transform, xSize, ySize)
        {
            this.HasNoDataValue = false;
            this.NoDataIsNaN = false;
            this.Name = name;
        }

        public abstract bool IsNoData(int xIndex, int yIndex);
    }

    public class RasterBand<TBand> : RasterBand where TBand : INumber<TBand>
    {
        public Memory<TBand> Data { get; private init; }
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
            this.NoDataIsNaN = TBand.IsNaN(this.NoDataValue);
        }

        public RasterBand(string name, SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, Memory<TBand> data)
            : base(name, crs, transform, xSize, ySize)
        {
            this.Data = data;
            // leave this.HasNoDataValue as false
            this.NoDataValue = Raster<TBand>.GetDefaultNoDataValue();
        }

        public TBand this[int cellIndex]
        {
            get { return this.Data.Span[cellIndex]; }
            set { this.Data.Span[cellIndex] = value; }
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
                return this.NoDataIsNaN ? TBand.IsNaN(value) : this.NoDataValue == value; // have to test with IsNaN() since float.NaN == float.NaN = false
            }
            return false;
        }

        public override bool IsNoData(int xIndex, int yIndex)
        {
            if (this.HasNoDataValue)
            {
                return this.IsNoData(this[xIndex, yIndex]);
            }
            return false;
        }

        public void SetNoDataValue(TBand noDataValue)
        {
            this.HasNoDataValue = true;
            this.NoDataIsNaN = TBand.IsNaN(this.NoDataValue);
            this.NoDataValue = noDataValue;
        }
    }
}
