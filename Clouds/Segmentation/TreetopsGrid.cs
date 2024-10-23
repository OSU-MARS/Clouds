using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;

namespace Mars.Clouds.Segmentation
{
    public class TreetopsGrid : Grid<Treetops>
    {
        public const int DefaultCellCapacity = 64; // cell size is likely around 0.1 ha => ~640 TPH basic capacity

        public int Treetops { get; set; }

        public TreetopsGrid(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells, bool cloneCrsAndTransform)
            : base(crs, transform, xSizeInCells, ySizeInCells, cloneCrsAndTransform)
        {
            this.Treetops = 0;

            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this[cellIndex] = new(TreetopsGrid.DefaultCellCapacity, xyIndices: true, classCapacity: 0);
            }
        }

        public void Reset(Grid dsmTile)
        {
            // inherited from Grid
            if ((this.SizeX != dsmTile.SizeX) || (this.SizeY != dsmTile.SizeY))
            {
                throw new NotSupportedException(nameof(this.Reset) + "() does not currently support changing the grid size from " + this.SizeX + " x " + this.SizeY + " cells to " + dsmTile.SizeX + " x " + dsmTile.SizeY + ".");
            }
            // for now, mostly caller's responsibility to ensure existing treetop cell size is suitable
            // DSM cell size is likely to be around 0.5 m and treetop cell size probably 10-20 m depending on species present so
            // useful checks on treetop versus DSM cell size are limited.
            if (Math.Sign(dsmTile.Transform.CellHeight) != Math.Sign(this.Transform.CellHeight))
            {
                // if needed, changes in cell height signs can be supported by recalculating the origin
                throw new NotSupportedException(dsmTile.Transform.CellHeight + " is not a valid cell height. Cell height must have the same sign as the current cell height (" + this.Transform.CellHeight + ").");
            }

            this.Transform.CopyOriginAndRotation(dsmTile.Transform);
            this.Treetops = 0;

            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this[cellIndex].Clear();
            }
        }
    }
}
