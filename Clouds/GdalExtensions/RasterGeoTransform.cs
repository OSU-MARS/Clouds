using OSGeo.GDAL;
using System;

namespace Mars.Clouds.GdalExtensions
{
    public class RasterGeoTransform
    {
        public double[] PadfTransform { get; private init; }

        public RasterGeoTransform(Dataset rasterDataset)
        {
            // https://gdal.org/api/gdaldataset_cpp.html#classGDALDataset_1a5101119705f5fa2bc1344ab26f66fd1d
            this.PadfTransform = new double[6];
            rasterDataset.GetGeoTransform(this.PadfTransform);

            if (Double.IsNaN(this.CellHeight) || (this.CellHeight == 0.0) || (Math.Abs(this.CellHeight) > 1000.0 * 1000.0) ||
                Double.IsNaN(this.CellWidth) || (this.CellWidth <= 0.0) ||
                Double.IsNaN(this.ColumnRotation) ||
                Double.IsNaN(this.OriginX) ||
                Double.IsNaN(this.OriginY) ||
                Double.IsNaN(this.RowRotation))
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset));
            }
        }

        // https://gdal.org/tutorials/geotransforms_tut.html
        public double CellHeight { get { return this.PadfTransform[5]; } } // north-south resolution, negative if north up
        public double CellWidth { get { return this.PadfTransform[1]; } } // east-west resolution
        public double ColumnRotation { get { return this.PadfTransform[4]; } } // zero if north up 
        public double OriginX { get { return this.PadfTransform[0]; } }
        public double OriginY { get { return this.PadfTransform[3]; } }
        public double RowRotation { get { return this.PadfTransform[2]; } } // zero if north up

        public (double x, double y) GetCellCenter(int rowIndex, int columnIndex)
        {
            double columnCenterIndex = columnIndex + 0.5;
            double rowCenterIndex = rowIndex + 0.5;

            // Xprojected = padfTransform[0] + pixelIndexX * padfTransform[1] + pixelIndexY * padfTransform[2];
            double x = this.OriginX + columnCenterIndex * this.CellWidth + rowCenterIndex * this.RowRotation;
            // Yprojected = padfTransform[3] + pixelIndexX * padfTransform[4] + pixelIndexY * padfTransform[5];
            double y = this.OriginY + columnCenterIndex * this.ColumnRotation + rowCenterIndex * this.CellHeight;
            return (x, y);
        }

        public (int rowIndex, int columnIndex) GetCellIndex(double x, double y)
        {
            double rowIndex = (y - this.OriginY - this.ColumnRotation / this.CellWidth * (x - this.OriginX)) / (this.CellHeight - this.ColumnRotation * this.RowRotation / this.CellWidth);
            double columnIndex = (x - this.OriginX - rowIndex * this.RowRotation) / this.CellWidth;
            return ((int)rowIndex, (int)columnIndex);
        }

        public (double x, double y) ToProjectedCoordinate(double xIndex, double yIndex)
        {
            // Xprojected = padfTransform[0] + pixelIndexX * padfTransform[1] + pixelIndexY * padfTransform[2];
            double x = this.OriginX + xIndex * this.CellWidth + yIndex * this.RowRotation;
            // Yprojected = padfTransform[3] + pixelIndexX * padfTransform[4] + pixelIndexY * padfTransform[5];
            double y = this.OriginY + xIndex * this.ColumnRotation + yIndex * this.CellHeight;
            return (x, y);
        }

    }
}
