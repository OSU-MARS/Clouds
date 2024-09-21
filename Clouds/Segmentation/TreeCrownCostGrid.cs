using Mars.Clouds.GdalExtensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Segmentation
{
    public class TreeCrownCostGrid : Grid<List<TreeCrownCostField>>
    {
        public TreeCrownCostNeighborhood? ActiveNeighborhood { get; private set; }

        public TreeCrownCostGrid(TreetopsGrid treetopsTile)
            : base(treetopsTile.Crs.Clone(), treetopsTile.Transform, treetopsTile.SizeX + 2, treetopsTile.SizeY, cloneCrsAndTransform: true)
        {
            // since cost grid includes neighboring cells, it is one cell larger in extent than the tile it covers
            // Offset the grid's origin accordingly.
            if ((this.Transform.RowRotation != 0.0) || (this.Transform.ColumnRotation != 0.0))
            {
                throw new NotSupportedException("Origin translation is not currently supported for rotated grids.");
            }
            this.Transform.SetOrigin(this.Transform.OriginX - this.Transform.CellWidth, this.Transform.OriginY - this.Transform.CellHeight); // works for positive and negative cell heights

            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this[cellIndex] = new(TreetopsGrid.DefaultCellCapacity);
            }

            this.ActiveNeighborhood = null;
        }

        [MemberNotNull(nameof(TreeCrownCostGrid.ActiveNeighborhood))]
        public void GetNeighborhood8(int cellIndexX, int cellIndexY, Neighborhood8<TreetopsGrid> treetopsNeighborhood)
        {
            if (this.ActiveNeighborhood == null)
            {
                this.ActiveNeighborhood = new(cellIndexX, cellIndexY, this);
            }
            else
            {
                this.ActiveNeighborhood.MoveTo(cellIndexX, cellIndexY, this);
            }
        }

        public void Reset(TreetopsGrid treetopsTile)
        {
            // inherited from Grid
            if ((this.SizeX != treetopsTile.SizeX) || (this.SizeY != treetopsTile.SizeY))
            {
                throw new NotSupportedException(nameof(this.Reset) + "() does not currently support changing the grid size from " + this.SizeX + " x " + this.SizeY + " cells to " + treetopsTile.SizeX + " x " + treetopsTile.SizeY + ".");
            }
            if ((treetopsTile.Transform.CellWidth != this.Transform.CellWidth) || (treetopsTile.Transform.CellHeight != this.Transform.CellHeight))
            {
                throw new NotSupportedException(nameof(this.Reset) + "() does not currently support changing the cell size from " + this.Transform.CellWidth + " x " + this.Transform.CellHeight + " cells to " + treetopsTile.Transform.CellWidth + " x " + treetopsTile.Transform.CellHeight + ".");
            }
            if ((treetopsTile.Transform.RowRotation != 0.0) || (treetopsTile.Transform.ColumnRotation != 0.0))
            {
                throw new NotSupportedException("Origin translation is not currently supported for rotated grids.");
            }

            this.Transform.SetOrigin(treetopsTile.Transform.OriginX - this.Transform.CellWidth, treetopsTile.Transform.OriginY - this.Transform.CellHeight); // works for positive and negative cell heights

            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this[cellIndex].Clear();
            }
        }

        public class TreeCrownCostNeighborhood : Neighborhood8<List<TreeCrownCostField>>, IEnumerable<TreeCrownCostField>
        {
            public TreeCrownCostNeighborhood(int indexX, int indexY, TreeCrownCostGrid costGrid)
                : base(indexX, indexY, costGrid)
            {
            }

            IEnumerator<TreeCrownCostField> IEnumerable<TreeCrownCostField>.GetEnumerator()
            {
                throw new NotImplementedException();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                throw new NotImplementedException();
            }
        }
    }
}
