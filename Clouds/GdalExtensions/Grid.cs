using OSGeo.OSR;
using System;
using System.Diagnostics;

namespace Mars.Clouds.GdalExtensions
{
    public class Grid
    {
        public SpatialReference Crs { get; protected set; }
        public GridGeoTransform Transform { get; private init; }
        public int SizeX { get; protected set; } // cells
        public int SizeY { get; protected set; } // cells

        protected Grid(Grid extent, bool cloneCrsAndTransform)
            : this(extent.Crs, extent.Transform, extent.SizeX, extent.SizeY, cloneCrsAndTransform)
        {
        }

        protected Grid(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells, bool cloneCrsAndTransform)
        {
            if (cloneCrsAndTransform)
            {
                this.Crs = crs.Clone();
                this.Transform = new(transform);
            }
            else
            {
                this.Crs = crs;
                this.Transform = transform;
            }

            this.SizeX = xSizeInCells;
            this.SizeY = ySizeInCells;
        }

        public int Cells
        {
            get { return this.SizeX * this.SizeY; }
        }

        public (double xCentroid, double yCentroid) GetCentroid()
        {
            double xCentroid = this.Transform.OriginX + 0.5 * this.SizeX * this.Transform.CellWidth;
            double yCentroid = this.Transform.OriginY + 0.5 * this.SizeX * this.Transform.CellHeight;
            return (xCentroid, yCentroid);
        }

        public (double xMin, double xMax, double yMin, double yMax) GetExtent()
        {
            Debug.Assert(this.Transform.CellHeight < 0.0);
            (double xMax, double yMin) = this.Transform.GetProjectedCoordinate(this.SizeX, this.SizeY);
            return (this.Transform.OriginX, xMax, yMin, this.Transform.OriginY);
        }

        public string GetExtentString()
        {
            double yMin = this.Transform.OriginY;
            double yMax = this.Transform.OriginY;
            double signedHeight = this.SizeY * this.Transform.CellHeight; // positive if cell height > 0, otherwise negative
            if (this.Transform.CellHeight < 0.0)
            {
                yMin += signedHeight;
            }
            else
            {
                yMax += signedHeight;
            }
            return this.Transform.OriginX + ", " + (this.Transform.OriginX + this.SizeX * this.Transform.CellWidth) + ", " + yMin + ", " + yMax;
        }

        public (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) GetIntersectingCellIndices(Extent extent)
        {
            return this.GetIntersectingCellIndices(extent.XMin, extent.XMax, extent.YMin, extent.YMax);
        }

        public (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) GetIntersectingCellIndices(double xMin, double xMax, double yMin, double yMax)
        {
            Debug.Assert(this.Transform.CellHeight < 0.0);
            (int xIndexMin, int yIndexMin) = this.ToGridIndices(xMin, yMax);
            (int xIndexMaxInclusive, int yIndexMaxInclusive) = this.ToGridIndices(xMax, yMin);
            
            if ((xIndexMin >= this.SizeX) || (xIndexMaxInclusive < 0) || (yIndexMin >= this.SizeY) || (yIndexMaxInclusive < 0))
            {
                (double gridXmin, double gridXmax, double gridYmin, double gridYmax) = this.GetExtent();
                throw new NotSupportedException("No intersection occurs between grid with extents (" + gridXmin + ", " + gridXmax + ", " + gridYmin + ", " + gridYmax + ") and area (" + xMin + ", " + xMax + ", " + yMin + ", " + yMax + ").");
            }
            
            if (xIndexMin < 0)
            {
                xIndexMin = 0;
            }
            if (xIndexMaxInclusive >= this.SizeX)
            {
                xIndexMaxInclusive = this.SizeX - 1;
            }
            if (yIndexMin < 0)
            {
                yIndexMin = 0;
            }
            if (yIndexMaxInclusive >= this.SizeY)
            {
                yIndexMaxInclusive = this.SizeY - 1;
            }

            return (xIndexMin, xIndexMaxInclusive, yIndexMin, yIndexMaxInclusive);
        }

        /// <remarks>Does not check CRS.</remarks>
        public bool IsSameExtentAndSpatialResolution(Grid other)
        {
            if ((this.SizeX != other.SizeX) || (this.SizeY != other.SizeY) || 
                (GridGeoTransform.Equals(this.Transform, other.Transform) == false))
            {
                return false;
            }

            return true;
        }

        public int ToCellIndex(int xIndex, int yIndex)
        {
            return xIndex + yIndex * this.SizeX;
        }

        // needs testing with nonzero row and column rotations
        public (int xIndex, int yIndex) ToInteriorGridIndices(double x, double y)
        {
            (double xIndexFractional, double yIndexFractional) = this.Transform.ToFractionalIndices(x, y);
            int xIndex = (int)xIndexFractional;
            int yIndex = (int)yIndexFractional;

            const double cellFractionTolerance = 0.000010; // 0.0000045 observed in practice
            if (xIndexFractional < 0.0)
            {
                if (xIndexFractional < -cellFractionTolerance)
                {
                    throw new NotSupportedException("Point at (x = " + x + ", y = " + y + " has an x value less than the grid's minimum x extent (" + this.GetExtentString() + " by a distance larger than can be attributed to numerical error.");
                }

                Debug.Assert(xIndex == 0); // cast to integer rounds toward zero
            }
            else if (xIndex >= this.SizeX)
            {
                if ((xIndex > this.SizeX) || (x > this.Transform.OriginX + this.Transform.CellWidth * (this.SizeX + cellFractionTolerance)))
                {
                    throw new NotSupportedException("Point at (x = " + x + ", y = " + y + " has an x value greater than the grid's maximum x extent (" + this.GetExtentString() + " by a distance larger than can be attributed to numerical error.");
                }

                xIndex -= 1; // if x lies exactly on or very close to grid edge, consider point part of the grid
            }
            // xIndex ∈ [ 0, this.XSize -1 ] falls through

            if (yIndexFractional < 0.0)
            {
                if (yIndexFractional < -cellFractionTolerance)
                {
                    throw new NotSupportedException("Point at (x = " + x + ", y = " + y + " lies outside the grid's y extent (" + this.GetExtentString() + " by a distance larger than can be attributed to numerical error.");
                }

                Debug.Assert(yIndex == 0); // cast to integer rounds toward zero
            }
            else if (yIndex >= this.SizeY)
            {
                // similarly, if y lies exactly on or very close grid edge consider point part of the grid
                if (this.Transform.CellHeight < 0.0)
                {
                    // y origin is grid's max y value
                    if ((yIndex > this.SizeY) || (y < this.Transform.OriginY + this.Transform.CellHeight * (this.SizeY + cellFractionTolerance)))
                    {
                        throw new NotSupportedException("Point at (x = " + x + ", y = " + y + " lies outside the grid's y extent (" + this.GetExtentString() + " by a distance larger than can be attributed to numerical error.");
                    }
                }
                else
                {
                    // y origin is grid's minimum y value
                    if ((yIndex > this.SizeY) || (y >= this.Transform.OriginY - cellFractionTolerance * this.Transform.CellHeight))
                    {
                        throw new NotSupportedException("Point at (x = " + x + ", y = " + y + " lies outside the grid's y extent (" + this.GetExtentString() + " by a distance larger than can be attributed to numerical error.");
                    }
                }

                yIndex -= 1;
            }
            // xIndex ∈ [ 0, this.XSize -1 ] falls through

            return (xIndex, yIndex);
        }

        public (int xIndex, int yIndex) ToGridIndices(double x, double y)
        {
            (double xIndexFractional, double yIndexFractional) = this.Transform.ToFractionalIndices(x, y);
            int xIndex = (int)xIndexFractional;
            int yIndex = (int)yIndexFractional;

            if (xIndexFractional < 0.0)
            {
                --xIndex; // integer truncation truncates towards zero
            }
            else if ((xIndex == this.SizeX) && (x == this.Transform.OriginX + this.Transform.CellWidth * this.SizeX))
            {
                xIndex -= 1; // if x lies exactly on grid edge, consider point part of the grid
            }

            if (yIndexFractional < 0.0)
            {
                --yIndex; // integer truncation truncates towards zero
            }
            else if (yIndex == this.SizeY)
            {
                // similarly, if y lies exactly on grid edge consider point part of the grid
                if (this.Transform.CellHeight < 0.0)
                {
                    if (y == this.Transform.OriginY + this.Transform.CellHeight * this.SizeX)
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
    }

    public class Grid<TCell> : Grid where TCell : class
    {
        protected TCell[] Data { get; private init; }

        public Grid(Grid extent)
            : this(extent, cloneCrsAndTransform: true)
        {
        }

        public Grid(Grid extent, bool cloneCrsAndTransform)
            : base(extent, cloneCrsAndTransform)
        {
            this.Data = new TCell[this.Cells];
        }

        public Grid(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells)
            : base(crs, transform, xSizeInCells, ySizeInCells, cloneCrsAndTransform: true)
        {
            this.Data = new TCell[this.Cells];
        }

        public TCell this[int cellIndex]
        {
            get { return this.Data[cellIndex]; }
            set { this.Data[cellIndex] = value; }
        }

        public TCell this[int xIndex, int yIndex]
        {
            get { return this[this.ToCellIndex(xIndex, yIndex)]; }
            set { this.Data[this.ToCellIndex(xIndex, yIndex)] = value; }
        }
    }
}
