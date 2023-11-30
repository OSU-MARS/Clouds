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

        protected Grid(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize)
        {
            crs.ExportToWkt(out string wkt, []);
            this.Crs = new(wkt);
            this.Transform = new(transform);
            this.XSize = xSize;
            this.YSize = ySize;
        }

        public (double xMin, double xMax, double yMin, double yMax) GetExtent()
        {
            Debug.Assert(this.Transform.CellHeight < 0.0);
            (double xMax, double yMin) = this.Transform.GetProjectedCoordinate(this.XSize, this.YSize);
            return (this.Transform.OriginX, xMax, yMin, this.Transform.OriginY);
        }

        public (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) GetIntersectingCellIndices(Extent extent)
        {
            return this.GetIntersectingCellIndices(extent.XMin, extent.XMax, extent.YMin, extent.YMax);
        }

        public (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) GetIntersectingCellIndices(double xMin, double xMax, double yMin, double yMax)
        {
            Debug.Assert(this.Transform.CellHeight < 0.0);
            (int xIndexMin, int yIndexMin) = this.Transform.GetCellIndices(xMin, yMax);
            (int xIndexMaxInclusive, int yIndexMaxInclusive) = this.Transform.GetCellIndices(xMax, yMin);
            
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

        public int ToCellIndex(int xIndex, int yIndex)
        {
            return xIndex + yIndex * this.XSize;
        }
    }

    public class Grid<TCell> : Grid where TCell : class
    {
        protected TCell?[] Cells { get; private init; }

        public int NonNullCells { get; protected set; }

        protected Grid(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize)
            : base(crs, transform, xSize, ySize)
        {
            this.Cells = new TCell?[xSize * ySize];
            this.NonNullCells = 0;
        }

        public TCell? this[int xIndex, int yIndex]
        {
            get { return this.Cells[xIndex + yIndex * this.XSize]; }
        }
    }
}
