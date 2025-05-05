using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;

namespace Mars.Clouds.Segmentation
{
    public class TreetopsGrid : Grid<TreetopsIndexed>
    {
        public const int DefaultCellCapacityInTreetops = 64; // cell size is likely around 0.1 ha => ~640 TPH basic capacity

        public int Treetops { get; set; }

        public TreetopsGrid(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells, bool cloneCrsAndTransform)
            : base(crs, transform, xSizeInCells, ySizeInCells, cloneCrsAndTransform)
        {
            this.Treetops = 0;

            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this[cellIndex] = new(TreetopsGrid.DefaultCellCapacityInTreetops);
            }
        }

        public static TreetopsGrid Create(Grid dsmTile)
        {
            // keep in sync with Reset()
            double treetopGridCellSizeX = TreeCrownCostField.CapacityXY * dsmTile.Transform.CellWidth;
            double treetopGridCellSizeY = TreeCrownCostField.CapacityXY * dsmTile.Transform.CellHeight; // default to same sign as DSM cell height
            (GridGeoTransform transform, int spanningSizeX, int spanningSizeY) = dsmTile.GetSpanningEquivalent(treetopGridCellSizeX, treetopGridCellSizeY);
            TreetopsGrid treetopTile = new(dsmTile.Crs.Clone(), transform, spanningSizeX, spanningSizeY, cloneCrsAndTransform: false);
            return treetopTile;
        }

        public void Reset(Grid dsmTile)
        {
            // keep in sync with Create()
            double treetopGridCellSizeX = TreeCrownCostField.CapacityXY * dsmTile.Transform.CellWidth;
            double treetopGridCellSizeY = TreeCrownCostField.CapacityXY * dsmTile.Transform.CellHeight;
            if (treetopGridCellSizeX != this.Transform.CellWidth)
            {
                throw new NotSupportedException(nameof(this.Reset) + "() does not currently support changing cell width from " + this.Transform.CellWidth + " to " + treetopGridCellSizeX + ".");
            }
            if (treetopGridCellSizeY != this.Transform.CellHeight)
            {
                throw new NotSupportedException(nameof(this.Reset) + "() does not currently support changing cell height from " + this.Transform.CellHeight + " to " + treetopGridCellSizeY + ".");
            }

            (GridGeoTransform transform, int spanningSizeX, int spanningSizeY) = dsmTile.GetSpanningEquivalent(treetopGridCellSizeX, treetopGridCellSizeY);
            if ((this.SizeX != spanningSizeX) || (this.SizeY != spanningSizeY))
            {
                throw new NotSupportedException(nameof(this.Reset) + "() does not currently support changing the grid size from " + this.SizeX + " x " + this.SizeY + " cells to " + dsmTile.SizeX + " x " + dsmTile.SizeY + ".");
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
