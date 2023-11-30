using OSGeo.GDAL;
using System;
using System.Diagnostics;

namespace Mars.Clouds.GdalExtensions
{
    public class GridGeoTransform
    {
        // https://gdal.org/tutorials/geotransforms_tut.html
        public double CellHeight { get; set; } // north-south resolution, negative if north up
        public double CellWidth { get; set; } // east-west resolution
        public double ColumnRotation { get; init; } // zero if north up 
        public double OriginX { get; set; }
        public double OriginY { get; set; }
        public double RowRotation { get; init; } // zero if north up

        public GridGeoTransform()
        {
            this.CellHeight = Double.NaN;
            this.CellWidth = Double.NaN;
            this.ColumnRotation = 0.0;
            this.OriginX = Double.NaN;
            this.OriginY = Double.NaN;
            this.RowRotation = 0.0;
        }

        public GridGeoTransform(double originX, double originY, double cellWidth, double cellHeight)
            : this(originX, originY, cellWidth, cellHeight, 0.0, 0.0)
        { 
        }

        public GridGeoTransform(double originX, double originY, double cellWidth, double cellHeight, double columnRotation, double rowRotation)
        {
            if (Double.IsFinite(originX) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(originX));
            }
            if (Double.IsFinite(originY) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(originY));
            }
            if ((cellWidth <= 0.0) || (Double.IsFinite(cellWidth) == false))
            {
                throw new ArgumentOutOfRangeException(nameof(cellWidth));
            }
            if ((cellHeight == 0.0) || (Double.IsFinite(cellHeight) == false))
            {
                throw new ArgumentOutOfRangeException(nameof(cellHeight));
            }
            if (Double.IsFinite(columnRotation) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(columnRotation));
            }
            if (Double.IsFinite(rowRotation) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(rowRotation));
            }

            this.OriginX = originX;
            this.OriginY = originY;
            this.CellWidth = cellWidth;
            this.CellHeight = cellHeight;
            this.RowRotation = rowRotation;
            this.ColumnRotation = columnRotation;
        }

        public GridGeoTransform(Dataset rasterDataset)
        {
            // https://gdal.org/api/gdaldataset_cpp.html#classGDALDataset_1a5101119705f5fa2bc1344ab26f66fd1d
            double[] padfTransform = new double[6];
            rasterDataset.GetGeoTransform(padfTransform);
            this.OriginX = padfTransform[0];
            this.CellWidth = padfTransform[1];
            this.RowRotation = padfTransform[2];
            this.OriginY = padfTransform[3];
            this.ColumnRotation = padfTransform[4];
            this.CellHeight = padfTransform[5];

            if ((Double.IsFinite(this.OriginX) == false) ||
                (Double.IsFinite(this.OriginY) == false) ||
                (this.CellWidth <= 0.0) || (Double.IsFinite(this.CellWidth) == false) ||
                (this.CellHeight == 0.0) || (Double.IsFinite(this.CellHeight) == false) ||
                (Double.IsFinite(this.ColumnRotation) == false) ||
                (Double.IsFinite(this.RowRotation) == false))
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset));
            }
        }

        public GridGeoTransform(GridGeoTransform other)
        {
            this.OriginX = other.OriginX;
            this.OriginY = other.OriginY;
            this.CellHeight = other.CellHeight;
            this.CellWidth = other.CellWidth;
            this.RowRotation = other.RowRotation;
            this.ColumnRotation = other.ColumnRotation;
        }

        public static bool Equals(GridGeoTransform transform, GridGeoTransform other)
        {
            // for now, require exact equality
            return (transform.OriginX == other.OriginX) &&
                   (transform.OriginY == other.OriginY) &&
                   (transform.CellHeight == other.CellHeight) &&
                   (transform.CellWidth == other.CellWidth) &&
                   (transform.RowRotation == other.RowRotation) &&
                   (transform.ColumnRotation == other.ColumnRotation);
        }

        public double GetCellArea()
        {
            return this.CellWidth * Double.Abs(this.CellHeight);
        }

        public (double x, double y) GetCellCenter(int xIndex, int yIndex)
        {
            return this.GetProjectedCoordinate(xIndex + 0.5, yIndex + 0.5);
        }

        public (double xMin, double xMax, double yMin, double yMax) GetCellExtent(int xIndex, int yIndex)
        {
            Debug.Assert(this.CellHeight < 0.0);
            (double xMin, double yMax) = this.GetProjectedCoordinate(xIndex, yIndex);
            return (xMin, xMin + this.CellWidth, yMax + this.CellHeight, yMax);
        }

        public (int xIndex, int yIndex) GetCellIndices(double x, double y)
        {
            double yIndexFractional = (y - this.OriginY - this.ColumnRotation / this.CellWidth * (x - this.OriginX)) / (this.CellHeight - this.ColumnRotation * this.RowRotation / this.CellWidth);
            double xIndexFractional = (x - this.OriginX - yIndexFractional * this.RowRotation) / this.CellWidth;

            int xIndex = (int)xIndexFractional;
            int yIndex = (int)yIndexFractional;
            // integer truncation truncates towards zero
            if (xIndexFractional < 0.0) 
            {
                --xIndex;
            }
            if (yIndexFractional < 0.0)
            {
                --yIndex;
            }
            return (xIndex, yIndex);
        }

        public double GetCellSize()
        {
            return 0.5 * (this.CellWidth + Double.Abs(this.CellHeight));
        }

        public double[] GetPadfTransform()
        {
            return [ this.OriginX, this.CellWidth, this.RowRotation, this.OriginY, this.ColumnRotation, this.CellHeight ];
        }

        public (double x, double y) GetProjectedCoordinate(double xIndexFractional, double yIndexFractional)
        {
            // Xprojected = padfTransform[0] + pixelIndexX * padfTransform[1] + pixelIndexY * padfTransform[2];
            double x = this.OriginX + xIndexFractional * this.CellWidth + yIndexFractional * this.RowRotation;
            // Yprojected = padfTransform[3] + pixelIndexX * padfTransform[4] + pixelIndexY * padfTransform[5];
            double y = this.OriginY + xIndexFractional * this.ColumnRotation + yIndexFractional * this.CellHeight;
            return (x, y);
        }
    }
}
