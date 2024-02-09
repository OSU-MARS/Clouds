using OSGeo.OSR;
using System;
using System.Diagnostics;

namespace Mars.Clouds.GdalExtensions
{
    public class Grid
    {
        public SpatialReference Crs { get; protected set; }
        public GridGeoTransform Transform { get; private init; }
        public int XSize { get; protected set; }
        public int YSize { get; protected set; }

        protected Grid(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells)
        {
            crs.ExportToWkt(out string wkt, []);
            this.Crs = new(wkt);
            this.Transform = new(transform);
            this.XSize = xSizeInCells;
            this.YSize = ySizeInCells;
        }

        public (int xIndex, int yIndex) GetCellIndices(double x, double y)
        {
            double yIndexFractional = (y - this.Transform.OriginY - this.Transform.ColumnRotation / this.Transform.CellWidth * (x - this.Transform.OriginX)) / (this.Transform.CellHeight - this.Transform.ColumnRotation * this.Transform.RowRotation / this.Transform.CellWidth);
            double xIndexFractional = (x - this.Transform.OriginX - yIndexFractional * this.Transform.RowRotation) / this.Transform.CellWidth;

            int xIndex = (int)xIndexFractional;
            int yIndex = (int)yIndexFractional;
            
            if (xIndexFractional < 0.0)
            {
                --xIndex; // integer truncation truncates towards zero
            }
            else if ((xIndex == this.XSize) && (x == this.Transform.OriginX + this.Transform.CellWidth * this.XSize))
            {
                xIndex -= 1; // if x lies exactly on grid edge, consider point part of the grid
            }

            if (yIndexFractional < 0.0)
            {
                --yIndex; // integer truncation truncates towards zero
            }
            else if (yIndex == this.YSize)
            {
                // similarly, if y lies exactly on grid edge consider point part of the grid
                if (this.Transform.CellHeight < 0.0)
                {
                    if (y == this.Transform.OriginY + this.Transform.CellHeight * this.XSize)
                    {
                        yIndex -= 1;
                    }
                }
                else
                {
                    if (y == this.Transform.OriginY)
                    {
                        yIndex -= 1;
                    }
                }
            }

            return (xIndex, yIndex);
        }

        public (double xCentroid, double yCentroid) GetCentroid()
        {
            double xCentroid = this.Transform.OriginX + 0.5 * this.XSize * this.Transform.CellWidth;
            double yCentroid = this.Transform.OriginY + 0.5 * this.XSize * this.Transform.CellHeight;
            return (xCentroid, yCentroid);
        }

        public (double xMin, double xMax, double yMin, double yMax) GetExtent()
        {
            Debug.Assert(this.Transform.CellHeight < 0.0);
            (double xMax, double yMin) = this.Transform.GetProjectedCoordinate(this.XSize, this.YSize);
            return (this.Transform.OriginX, xMax, yMin, this.Transform.OriginY);
        }

        public string GetExtentString()
        {
            double yMin = this.Transform.OriginY;
            double yMax = this.Transform.OriginY;
            double signedHeight = this.YSize * this.Transform.CellHeight; // positive if cell height > 0, otherwise negative
            if (this.Transform.CellHeight < 0.0)
            {
                yMin += signedHeight;
            }
            else
            {
                yMax += signedHeight;
            }
            return this.Transform.OriginX + ", " + (this.Transform.OriginX + this.XSize * this.Transform.CellWidth) + ", " + yMin + ", " + yMax;
        }

        public (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) GetIntersectingCellIndices(Extent extent)
        {
            return this.GetIntersectingCellIndices(extent.XMin, extent.XMax, extent.YMin, extent.YMax);
        }

        public (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) GetIntersectingCellIndices(double xMin, double xMax, double yMin, double yMax)
        {
            Debug.Assert(this.Transform.CellHeight < 0.0);
            (int xIndexMin, int yIndexMin) = this.GetCellIndices(xMin, yMax);
            (int xIndexMaxInclusive, int yIndexMaxInclusive) = this.GetCellIndices(xMax, yMin);
            
            if ((xIndexMin >= this.XSize) || (xIndexMaxInclusive < 0) || (yIndexMin >= this.YSize) || (yIndexMaxInclusive < 0))
            {
                (double gridXmin, double gridXmax, double gridYmin, double gridYmax) = this.GetExtent();
                throw new NotSupportedException("No intersection occurs between grid with extents (" + gridXmin + ", " + gridXmax + ", " + gridYmin + ", " + gridYmax + ") and area (" + xMin + ", " + xMax + ", " + yMin + ", " + yMax + ").");
            }
            
            if (xIndexMin < 0)
            {
                xIndexMin = 0;
            }
            if (xIndexMaxInclusive >= this.XSize)
            {
                xIndexMaxInclusive = this.XSize - 1;
            }
            if (yIndexMin < 0)
            {
                yIndexMin = 0;
            }
            if (yIndexMaxInclusive >= this.YSize)
            {
                yIndexMaxInclusive = this.YSize - 1;
            }

            return (xIndexMin, xIndexMaxInclusive, yIndexMin, yIndexMaxInclusive);
        }

        public bool IsSameExtent(Grid other)
        {
            (double thisXmin, double thisXmax, double thisYmin, double thisYmax) = this.GetExtent();
            (double otherXmin, double otherXmax, double otherYmin, double otherYmax) = other.GetExtent();

            // for now use exact equality
            return (thisXmin == otherXmin) && (thisXmax == otherXmax) && (thisYmin == otherYmin) && (thisYmax == otherYmax);
        }

        public int ToCellIndex(int xIndex, int yIndex)
        {
            return xIndex + yIndex * this.XSize;
        }
    }

    public class Grid<TCell> : Grid where TCell : class?
    {
        protected TCell?[] Cells { get; private init; }

        public int NonNullCells { get; protected set; }

        public Grid(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells)
            : base(crs, transform, xSizeInCells, ySizeInCells)
        {
            this.Cells = new TCell?[xSizeInCells * ySizeInCells];
            this.NonNullCells = 0;
        }

        public TCell? this[int cellIndex]
        {
            get { return this.Cells[cellIndex]; }
            set { this.Cells[cellIndex] = value; }
        }

        public TCell? this[int xIndex, int yIndex]
        {
            get { return this[this.ToCellIndex(xIndex, yIndex)]; }
            set { this.Cells[this.ToCellIndex(xIndex, yIndex)] = value; }
        }
    }
}
