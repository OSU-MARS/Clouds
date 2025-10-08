using OSGeo.OSR;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.GdalExtensions
{
    public class Grid
    {
        public SpatialReference Crs { get; set; }
        public GridGeoTransform Transform { get; protected set; }
        public int SizeX { get; protected set; } // cells
        public int SizeY { get; protected set; } // cells

        protected Grid(Grid transformAndExtent, bool cloneCrsAndTransform)
            : this(transformAndExtent.Crs, transformAndExtent.Transform, transformAndExtent.SizeX, transformAndExtent.SizeY, cloneCrsAndTransform)
        {
        }

        protected Grid(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells, bool cloneCrsAndTransform)
        {
            Debug.Assert((xSizeInCells > 0) && (ySizeInCells > 0), "Grid must contain at least one cell.");

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
            double yCentroid = this.Transform.OriginY + 0.5 * this.SizeY * this.Transform.CellHeight;
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
            return $"{this.Transform.OriginX}, {this.Transform.OriginX + this.SizeX * this.Transform.CellWidth}, {yMin}, {yMax}";
        }

        public (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) GetIntersectingCellIndices(Extent extent)
        {
            return this.GetIntersectingCellIndices(extent.XMin, extent.XMax, extent.YMin, extent.YMax);
        }

        public (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) GetIntersectingCellIndices(double xMin, double xMax, double yMin, double yMax)
        {
            if (this.Transform.ColumnRotation != 0.0)
            {
                throw new NotSupportedException($"Rotated grids are not currently handled by {nameof(this.GetIntersectingCellIndices)}().");
            }

            (int xIndexMin, int yIndexMin) = this.ToGridIndices(xMin, yMax);
            (int xIndexMaxInclusive, int yIndexMaxInclusive) = this.ToGridIndices(xMax, yMin);
            
            if ((xIndexMin >= this.SizeX) || (xIndexMaxInclusive < 0) || (yIndexMin >= this.SizeY) || (yIndexMaxInclusive < 0))
            {
                (double gridXmin, double gridXmax, double gridYmin, double gridYmax) = this.GetExtent();
                throw new NotSupportedException($"No intersection occurs between grid with extents ({gridXmin}, {gridXmax}, {gridYmin}, {gridYmax}) and area ({xMin}, {xMax}, {yMin}, {yMax}).");
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

        /// <summary>
        /// Get a grid transform and size which covers the same extent as this grid.
        /// </summary>
        /// <remarks>
        /// If the new cell size is not an exact multiple of the current x and y extent the returned sizes are rounded up to
        /// extend beyond the current size.
        /// </remarks>
        public (GridGeoTransform transform, int spanningSizeX, int spanningSizeY) GetSpanningEquivalent(double newCellWidth, double newCellHeight)
        {
            if (newCellWidth < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(newCellWidth), $"{newCellWidth} is not a valid cell width. Cell sizes must be positive.");
            }
            if ((Math.Sign(newCellHeight) != Math.Sign(this.Transform.CellHeight)) || (newCellHeight == 0.0))
            {
                // if needed, changes in cell height signs can be supported by recalculating the origin and ensuring spanningSizeY is positive
                throw new ArgumentOutOfRangeException(nameof(newCellHeight), $"{newCellHeight} is not a supported cell height. Cell height must have the same sign as the current cell height ({this.Transform.CellHeight}) and be nonzero.");
            }

            GridGeoTransform transform = new(this.Transform.OriginX, this.Transform.OriginY, newCellWidth, newCellHeight);
            int spanningSizeX = (int)Math.Ceiling(this.SizeX * this.Transform.CellWidth / newCellWidth);
            int spanningSizeY = (int)Math.Ceiling(this.SizeY * this.Transform.CellHeight / newCellHeight);
            return (transform, spanningSizeX, spanningSizeY);
        }

        public bool IsSameExtent(Extent other)
        {
            if ((this.Transform.OriginX == other.XMin) && (this.SizeX * this.Transform.CellWidth == other.Width))
            {
                double cellHeight = this.Transform.CellHeight;
                if (this.Transform.CellHeight < 0.0)
                {
                    if (this.Transform.OriginY != other.YMax)
                    {
                        return false;
                    }

                    cellHeight = -cellHeight;
                }
                else
                {
                    if (this.Transform.OriginY != other.YMin)
                    {
                        return false;
                    }
                }

                return this.SizeY * cellHeight == other.Height;
            }

            return false;
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

        /// <remarks>Does not check CRS.</remarks>
        public bool IsSameExtentAndTileResolution(VirtualRaster vrt)
        {
            if ((this.SizeX != vrt.SizeInTilesX) || (this.SizeY != vrt.SizeInTilesY) ||
                (GridGeoTransform.Equals(this.Transform, vrt.TileTransform) == false))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the one dimensional cell index matching an x and y position within the grid.
        /// </summary>
        /// <param name="xIndex">x index located within the grid</param>
        /// <param name="yIndex">y index located within the grid</param>
        /// <remarks>
        /// Passing an <paramref name="xIndex"/> which is off, but reasonably close to, the grid will return a cell index within
        /// the length of the grid's data array, possibly resulting in incorrect caller behavior. How far off the grid this can
        /// occur depends on <paramref name="yIndex"/>. If <paramref name="yIndex"/> is zero then <paramref name="xIndex"/> up
        /// to but not including <see cref="Cells"/> will return within length cell indices. As <paramref name="yIndex"/> increases
        /// this caller misuable extension in the +x direction shifts to the -x direction.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ToCellIndex(int xIndex, int yIndex)
        {
            Debug.Assert((0 <= xIndex) && (xIndex < this.SizeX) && (0 <= yIndex) && (yIndex < this.SizeY));
            return xIndex + yIndex * this.SizeX;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Int64 ToCellIndex(Int64 xIndex, Int64 yIndex)
        {
            Debug.Assert((0 <= xIndex) && (xIndex < this.SizeX) && (0 <= yIndex) && (yIndex < this.SizeY));
            return xIndex + yIndex * this.SizeX;
        }

        /// <summary>
        /// Convert a position to a cell index that might or might not be on the grid.
        /// </summary>
        /// <param name="x">x coordinate in grid's CRS.</param>
        /// <param name="y">y coordinate in grid's CRS.</param>
        /// <returns>An (x, y) index tuple whose values will lie on the grid if <paramref name="x"/> and <paramref name="y"/> lie within the grid.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                // TODO: support rotated rasters
                xIndex -= 1; // if x lies exactly on grid edge, consider point part of the grid
            }

            if (yIndexFractional < 0.0)
            {
                --yIndex; // integer truncation truncates towards zero
            }
            else if (yIndex == this.SizeY)
            {
                // similarly, if y lies exactly on grid edge consider point part of the grid
                // TODO: support rotated rasters
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

        /// <summary>
        /// Convert a cell index to x and y indices which might or might not be on the grid.
        /// </summary>
        /// <remarks>
        /// If <paramref name="cellIndex"/> is between zero and <see cref="Cells"/> an on grid position is returned. Cell indices
        /// beyond this range yield off grid positions.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (int xIndex, int yIndex) ToGridIndices(int cellIndex)
        {
            Debug.Assert(cellIndex >= 0);
            int yIndex = cellIndex / this.SizeX;
            int xIndex = cellIndex - this.SizeX * yIndex;
            return (xIndex, yIndex);
        }

        // needs testing with nonzero row and column rotations
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                    throw new NotSupportedException($"Point at (x = {x}, y = {y} has an x value less than the grid's minimum x extent ({this.GetExtentString()} by a distance larger than can be attributed to numerical error.");
                }

                Debug.Assert(xIndex == 0); // cast to integer rounds toward zero
            }
            else if (xIndex >= this.SizeX)
            {
                if ((xIndex > this.SizeX) || (x > this.Transform.OriginX + this.Transform.CellWidth * (this.SizeX + cellFractionTolerance)))
                {
                    throw new NotSupportedException($"Point at (x = {x}, y = {y} has an x value greater than the grid's maximum x extent ({this.GetExtentString()} by a distance larger than can be attributed to numerical error.");
                }

                xIndex -= 1; // if x lies exactly on or very close to grid edge, consider point part of the grid
            }
            // xIndex ∈ [ 0, this.XSize -1 ] falls through

            if (yIndexFractional < 0.0)
            {
                if (yIndexFractional < -cellFractionTolerance)
                {
                    throw new NotSupportedException($"Point at (x = {x}, y = {y} lies outside the grid's y extent ({this.GetExtentString()} by a distance larger than can be attributed to numerical error.");
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
                        throw new NotSupportedException($"Point at (x = {x}, y = {y} lies outside the grid's y extent ({this.GetExtentString()} by a distance larger than can be attributed to numerical error.");
                    }
                }
                else
                {
                    // y origin is grid's minimum y value
                    if ((yIndex > this.SizeY) || (y >= this.Transform.OriginY - cellFractionTolerance * this.Transform.CellHeight))
                    {
                        throw new NotSupportedException($"Point at (x = {x}, y = {y} lies outside the grid's y extent ({this.GetExtentString()} by a distance larger than can be attributed to numerical error.");
                    }
                }

                yIndex -= 1;
            }
            // xIndex ∈ [ 0, this.XSize -1 ] falls through

            return (xIndex, yIndex);
        }
    }

    /// <remarks>
    /// Constructors are protected because either a <see cref="TCell"/> : class, new() or a derived class is needed to ensure all elements in <see cref="Data"> are non-null after returning.
    /// </remarks>
    public class Grid<TCell> : Grid
    {
        protected TCell[] Data { get; private init; }

        protected Grid(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells, bool cloneCrsAndTransform)
            : base(crs, transform, xSizeInCells, ySizeInCells, cloneCrsAndTransform)
        {
            this.Data = new TCell[this.Cells];
        }

        public TCell this[int cellIndex]
        {
            get { return this.Data[cellIndex]; }
            set { this.Data[cellIndex] = value; }
        }

        public TCell this[Int64 cellIndex]
        {
            get { return this.Data[cellIndex]; }
            set { this.Data[cellIndex] = value; }
        }

        public TCell this[int xIndex, int yIndex]
        {
            get { return this.Data[this.ToCellIndex(xIndex, yIndex)]; }
            set { this.Data[this.ToCellIndex(xIndex, yIndex)] = value; }
        }

        public TCell this[Int64 xIndex, Int64 yIndex]
        {
            get { return this.Data[this.ToCellIndex(xIndex, yIndex)]; }
            set { this.Data[this.ToCellIndex(xIndex, yIndex)] = value; }
        }
    }
}
