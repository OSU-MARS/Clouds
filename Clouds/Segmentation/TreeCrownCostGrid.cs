﻿using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mars.Clouds.Segmentation
{
    public class TreeCrownCostGrid : Grid<List<TreeCrownCostField>>
    {
        private Neighborhood8<List<TreeCrownCostField>>? currentNeighborhood;

        public List<TreeCrownCostField> ActiveFields { get; private init; }

        public TreeCrownCostGrid(TreetopsGrid treetopsTile)
            : base(treetopsTile.Crs.Clone(), treetopsTile.Transform, treetopsTile.SizeX + 2, treetopsTile.SizeY + 2, cloneCrsAndTransform: true)
        {
            // since cost grid includes neighboring cells, it is one cell larger in extent than the tile it covers
            // Offset the grid's origin accordingly.
            if ((this.Transform.RowRotation != 0.0) || (this.Transform.ColumnRotation != 0.0))
            {
                throw new NotSupportedException("Origin translation is not currently supported for rotated grids.");
            }
            this.Transform.SetOrigin(this.Transform.OriginX - this.Transform.CellWidth, this.Transform.OriginY - this.Transform.CellHeight); // works for positive and negative cell heights

            this.currentNeighborhood = null;
            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this[cellIndex] = new(TreetopsGrid.DefaultCellCapacity);
            }

            this.ActiveFields = [];
        }

        private void AddIntersectingFields(List<TreeCrownCostField> costFields, TreeCrownSegmentationState segmentationState)
        {
            int dsmStartIndexX = segmentationState.DsmStartIndexX;
            int dsmStartIndexY = segmentationState.DsmStartIndexY;
            int dsmEndIndexX = segmentationState.DsmEndIndexX;
            int dsmEndIndexY = segmentationState.DsmEndIndexY;
            for (int fieldIndex = 0; fieldIndex < costFields.Count; ++fieldIndex)
            {
                TreeCrownCostField costField = costFields[fieldIndex];
                if (costField.Intersects(dsmStartIndexX, dsmEndIndexX, dsmStartIndexY, dsmEndIndexY))
                {
                    this.ActiveFields.Add(costField);
                }
            }
        }

        private static void CostCell(Treetops cellTreetops, List<TreeCrownCostField> cellCostFields, TreeCrownSegmentationState segmentationState)
        {
            ObjectPool<TreeCrownCostField> costFieldPool = segmentationState.FieldPool;
            for (int treetopIndex = 0; treetopIndex < cellTreetops.Count; ++treetopIndex)
            {
                if (costFieldPool.TryGet(out TreeCrownCostField? costField) == false)
                {
                    costField = new();
                }

                int treetopDsmIndexX = cellTreetops.XIndex[treetopIndex];
                int treetopDsmIndexY = cellTreetops.YIndex[treetopIndex];
                int treetopID = cellTreetops.ID[treetopIndex];
                float treetopDsmZ = (float)(cellTreetops.Elevation[treetopIndex] + cellTreetops.Height[treetopIndex]);
                costField.MoveToTreetopAndRecalculate(treetopDsmIndexX, treetopDsmIndexY, treetopID, treetopDsmZ, segmentationState);
                cellCostFields.Add(costField);
            }
        }

        public void EnumerateCostFields(int treetopCellIndexX, int treetopCellIndexY, TreeCrownSegmentationState segmentationState)
        {
            if ((treetopCellIndexX < 0) || (treetopCellIndexX >= this.SizeX - 1))
            {
                throw new ArgumentOutOfRangeException(nameof(treetopCellIndexX), nameof(treetopCellIndexX) + " must be in [ 0, " + (this.SizeX - 2) + "] to indicate an eight-way neighborhood that fully within the cost grid (grid is " + this.SizeX + " by " + this.SizeY + ").");
            }
            if ((treetopCellIndexY < 0) || (treetopCellIndexY >= this.SizeY - 1))
            {
                throw new ArgumentOutOfRangeException(nameof(treetopCellIndexY), nameof(treetopCellIndexY) + " must be in [ 0, " + (this.SizeY - 2) + "] to indicate an eight-way neighborhood that fully within the cost grid (grid is " + this.SizeX + " by " + this.SizeY + ").");
            }

            GridNeighborhood8<TreetopsGrid, Treetops>? treetopsNeighborhood = segmentationState.TreetopNeighborhood;
            if (treetopsNeighborhood == null)
            {
                throw new ArgumentOutOfRangeException(nameof(segmentationState), "Segmentation state's treetop neighborhood is missing. Call " + nameof(segmentationState.SetNeighborhoods) + "() before calling " + nameof(this.EnumerateCostFields) + "().");
            }

            int cellIndexX = treetopCellIndexX + 1;
            int cellIndexY = treetopCellIndexY + 1;
            if (this.currentNeighborhood == null)
            {
                this.currentNeighborhood = new(cellIndexX, cellIndexY, this);
            }
            else
            {
                this.currentNeighborhood.MoveTo(cellIndexX, cellIndexY, this);
            }

            // ensure cost fields are instantiated for neighborhood
            // If cells are traversed usual order (west to east, north to south) after the first call only the southeast cell
            // will have new fields to calculate.
            Debug.Assert((this.currentNeighborhood.Northwest != null) && (this.currentNeighborhood.North != null) && (this.currentNeighborhood.Northeast != null) &&
                         (this.currentNeighborhood.West != null) && (this.currentNeighborhood.East != null) &&
                         (this.currentNeighborhood.Southwest != null) && (this.currentNeighborhood.South != null) && (this.currentNeighborhood.Southeast != null));
            if ((this.currentNeighborhood.Northwest.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX - 1, treetopCellIndexY - 1, out Treetops? treetopsNorthwest))
            {
                TreeCrownCostGrid.CostCell(treetopsNorthwest, this.currentNeighborhood.Northwest, segmentationState);
            }
            if ((this.currentNeighborhood.North.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX, treetopCellIndexY - 1, out Treetops? treetopsNorth))
            {
                TreeCrownCostGrid.CostCell(treetopsNorth, this.currentNeighborhood.North, segmentationState);
            }
            if ((this.currentNeighborhood.Northeast.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX + 1, treetopCellIndexY - 1, out Treetops? treetopsNortheast))
            {
                TreeCrownCostGrid.CostCell(treetopsNortheast, this.currentNeighborhood.Northeast, segmentationState);
            }

            if ((this.currentNeighborhood.West.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX - 1, treetopCellIndexY, out Treetops? treetopsWest))
            {
                TreeCrownCostGrid.CostCell(treetopsWest, this.currentNeighborhood.West, segmentationState);
            }
            if ((this.currentNeighborhood.Center.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX, treetopCellIndexY, out Treetops? treetopsCenter))
            {
                TreeCrownCostGrid.CostCell(treetopsCenter, this.currentNeighborhood.Center, segmentationState);
            }
            if ((this.currentNeighborhood.East.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX + 1, treetopCellIndexY, out Treetops? treetopsEast))
            {
                TreeCrownCostGrid.CostCell(treetopsEast, this.currentNeighborhood.East, segmentationState);
            }

            if ((this.currentNeighborhood.Southwest.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX - 1, treetopCellIndexY + 1, out Treetops? treetopsSouthwest))
            {
                TreeCrownCostGrid.CostCell(treetopsSouthwest, this.currentNeighborhood.Southwest, segmentationState);
            }
            if ((this.currentNeighborhood.South.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX, treetopCellIndexY + 1, out Treetops? treetopsSouth))
            {
                TreeCrownCostGrid.CostCell(treetopsSouth, this.currentNeighborhood.South, segmentationState);
            }
            if ((this.currentNeighborhood.Southeast.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX + 1, treetopCellIndexY + 1, out Treetops? treetopsSoutheast))
            {
                TreeCrownCostGrid.CostCell(treetopsSoutheast, this.currentNeighborhood.Southeast, segmentationState);
            }

            // update active field list to fields overlapping the new center
            this.ActiveFields.Clear();

            this.AddIntersectingFields(this.currentNeighborhood.Northwest, segmentationState);
            this.AddIntersectingFields(this.currentNeighborhood.North, segmentationState);
            this.AddIntersectingFields(this.currentNeighborhood.Northeast, segmentationState);

            this.AddIntersectingFields(this.currentNeighborhood.West, segmentationState);
            this.ActiveFields.AddRange(this.currentNeighborhood.Center);
            this.AddIntersectingFields(this.currentNeighborhood.East, segmentationState);

            this.AddIntersectingFields(this.currentNeighborhood.Southwest, segmentationState);
            this.AddIntersectingFields(this.currentNeighborhood.South, segmentationState);
            this.AddIntersectingFields(this.currentNeighborhood.Southeast, segmentationState);
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

        public void Return(int treetopCellIndexX, int treetopCellIndexY, TreeCrownSegmentationState segmentationState)
        {
            int cellIndexX = treetopCellIndexX + 1;
            int cellIndexY = treetopCellIndexY + 1;
            List<TreeCrownCostField> costFields = this[cellIndexX, cellIndexY];
            ObjectPool<TreeCrownCostField> costFieldPool = segmentationState.FieldPool;
            for (int fieldIndex = 0; fieldIndex < costFields.Count; ++fieldIndex)
            {
                TreeCrownCostField costField = costFields[fieldIndex];
                costFieldPool.Return(costField);
            }

            costFields.Clear();
        }
    }
}