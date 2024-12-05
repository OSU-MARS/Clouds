using Mars.Clouds.Extensions;
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
                this[cellIndex] = new(TreetopsGrid.DefaultCellCapacityInTreetops);
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

        private static void CostCell(Treetops cellTreetops, List<TreeCrownCostField> cellCostFields, int dsmNeighborhoodOffsetX, int dsmNeighborhoodOffsetY, TreeCrownSegmentationState segmentationState)
        {
            Debug.Assert(segmentationState.DsmNeighborhood != null);

            ObjectPool<TreeCrownCostField> costFieldPool = segmentationState.FieldPool;
            RasterBand<float> dsmTile = segmentationState.DsmNeighborhood.Center;
            int maxDsmOverlapIndexX = dsmTile.SizeX + TreeCrownCostField.CapacityRadius;
            int maxDsmOverlapIndexY = dsmTile.SizeY + TreeCrownCostField.CapacityRadius;
            for (int treetopIndex = 0; treetopIndex < cellTreetops.Count; ++treetopIndex)
            {
                // if this is an on tile cell, all treetops yield cost fields as they're all on tile
                // if this is a tile adjacent cell, ~50% or less of treetops are within range of the tile
                // Currently, the crown cost grid is aligned to the DSM tile's origin and therefore has one full row north of and one
                // full column west of the tile. Along the tiles' south and east sides the cost grid extends at least a full row and
                // column, potentially up to just under two rows and columns (<2 as opposed to ≤2), depending on how close the tile
                // size is to an integer multiple of the cost cell size. Thus,
                //
                // - Trees in adjacent cells along the tile's north and west sides have cost fields whose maximum extent overlaps part
                //   of the tile with at most the probability TreeCrownCostField.CapacityRadius / TreeCrownCostField.CapacityXY (49% with
                //   a radius capacity of 31 cells and xy capacity of 63 cells). For the cell diagonally off the tile's northwest corner
                //   this probability is halved.
                // - Some trees in the last row and column of cells including the tiles' south and east sides might not lie within cost
                //   field range of the tile, depending on grid sizing. Cost fields of trees in the adjacent south row and east column
                //   potentially overlap the tile at probabilities lower than the adjacent north row and west column. Corner probabilities
                //   are similarly further reduced.
                // - Since most trees' cost fields do not extend to a field's maximum size, actual tile overlap probabilities are lower.
                //
                // Since treetops grid tiles are a virtual vector streamed through memory, each grid cell must be loaded with all of the
                // treetops it contains in order to correctly segment the tile the cell lies on. For adjacent tiles, where such cells
                // become adjacent cells, there are two main implementation options.
                //
                // 1. Allow RasterNeighborhood8<T>.Slice() to generate slices which do not intersect the tile, incuring the overhead of
                //    costing and hit testing trees whose fields are guaranteed not to intersect the tile.
                // 2. Skip cost field generation for trees which are guaranteed not to intersect in this loop, avoiding at least half of
                //    the costing and testing overhead.
                //
                // The latter is implemented here, though the further reduction of not adding non-intersecting cost fields is not currently
                // taken.
                int treetopDsmNeighborhoodIndexX = cellTreetops.XIndex[treetopIndex] + dsmNeighborhoodOffsetX;
                if ((treetopDsmNeighborhoodIndexX < -TreeCrownCostField.CapacityRadius) || (treetopDsmNeighborhoodIndexX >= maxDsmOverlapIndexX))
                {
                    continue; // field can't overlap tile
                }
                int treetopDsmNeighborhoodIndexY = cellTreetops.YIndex[treetopIndex] + dsmNeighborhoodOffsetY;
                if ((treetopDsmNeighborhoodIndexY < -TreeCrownCostField.CapacityRadius) || (treetopDsmNeighborhoodIndexY >= maxDsmOverlapIndexY))
                {
                    continue; // field can't overlap tile
                }

                if (costFieldPool.TryGet(out TreeCrownCostField? costField) == false)
                {
                    costField = new();
                }

                int treetopID = cellTreetops.ID[treetopIndex];
                double dtmZ = cellTreetops.Elevation[treetopIndex];
                double treetopHeight = cellTreetops.Height[treetopIndex];
                float treetopDsmZ = (float)(dtmZ + treetopHeight);
                costField.MoveToTreetopAndRecalculate(treetopDsmNeighborhoodIndexX, treetopDsmNeighborhoodIndexY, treetopID, treetopDsmZ, (float)treetopHeight, segmentationState);
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
                throw new ArgumentOutOfRangeException(nameof(segmentationState), "Segmentation state's treetop neighborhood is missing. Call " + nameof(segmentationState.SetNeighborhoodsAndCellSize) + "() before calling " + nameof(this.EnumerateCostFields) + "().");
            }

            int cellIndexX = treetopCellIndexX + 1; // guaranteed to be in [ 1, this.SizeX ]
            int cellIndexY = treetopCellIndexY + 1; // guaranteed to be in [ 1, this.SizeY ]
            if (this.currentNeighborhood == null)
            {
                this.currentNeighborhood = new(cellIndexX, cellIndexY, this);
            }
            else
            {
                this.currentNeighborhood.MoveTo(cellIndexX, cellIndexY, this);
            }

            // ensure cost fields are instantiated for neighborhood
            // If cells are traversed in the usual order (west to east, north to south) after the first call only the southeast
            // cell will have new fields to calculate.
            // If an adjacent cell in the tree crown grid (quadtree) lies off the central tile then its DSM neighborhood indices
            // differ from its treetops' DSM indices because the neighborhood is centered on a different tile. To correclty place
            // those treetops' fields, CostCell() needs to know the tile to tile DSM offset.
            Debug.Assert((segmentationState.DsmNeighborhood != null) &&
                         (this.currentNeighborhood.Northwest != null) && (this.currentNeighborhood.North != null) && (this.currentNeighborhood.Northeast != null) &&
                         (this.currentNeighborhood.West != null) && (this.currentNeighborhood.East != null) &&
                         (this.currentNeighborhood.Southwest != null) && (this.currentNeighborhood.South != null) && (this.currentNeighborhood.Southeast != null));
            int treetopCellIndexNorth = treetopCellIndexY - 1;
            int treetopCellIndexSouth = treetopCellIndexY + 1;
            int treetopCellIndexWest = treetopCellIndexX - 1;
            int treetopCellIndexEast = treetopCellIndexX + 1;
            int dsmNeighborhoodOffsetNorth = treetopCellIndexNorth < 0 ? (segmentationState.DsmNeighborhood.North != null ? -segmentationState.DsmNeighborhood.North.SizeY : Int32.MinValue) : 0;
            int dsmNeighborhoodOffsetSouth = treetopCellIndexSouth >= (this.SizeY - 2) ? segmentationState.DsmNeighborhood.Center.SizeY : 0;
            int dsmNeighborhoodOffsetEast = treetopCellIndexEast >= (this.SizeX - 2) ? segmentationState.DsmNeighborhood.Center.SizeX : 0;
            int dsmNeighborhoodOffsetWest = treetopCellIndexWest < 0 ? (segmentationState.DsmNeighborhood.West != null ? -segmentationState.DsmNeighborhood.West.SizeX : Int32.MinValue) : 0;
            if ((this.currentNeighborhood.Northwest.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexWest, treetopCellIndexNorth, out Treetops? treetopsNorthwest))
            {
                TreeCrownCostGrid.CostCell(treetopsNorthwest, this.currentNeighborhood.Northwest, dsmNeighborhoodOffsetWest, dsmNeighborhoodOffsetNorth, segmentationState);
            }
            if ((this.currentNeighborhood.North.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX, treetopCellIndexNorth, out Treetops? treetopsNorth))
            {
                TreeCrownCostGrid.CostCell(treetopsNorth, this.currentNeighborhood.North, 0, dsmNeighborhoodOffsetNorth, segmentationState);
            }
            if ((this.currentNeighborhood.Northeast.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX + 1, treetopCellIndexNorth, out Treetops? treetopsNortheast))
            {
                TreeCrownCostGrid.CostCell(treetopsNortheast, this.currentNeighborhood.Northeast, dsmNeighborhoodOffsetEast, dsmNeighborhoodOffsetNorth, segmentationState);
            }

            if ((this.currentNeighborhood.West.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexWest, treetopCellIndexY, out Treetops? treetopsWest))
            {
                TreeCrownCostGrid.CostCell(treetopsWest, this.currentNeighborhood.West, dsmNeighborhoodOffsetWest, 0, segmentationState);
            }
            if ((this.currentNeighborhood.Center.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX, treetopCellIndexY, out Treetops? treetopsCenter))
            {
                // center of neighborhood is always on tile
                TreeCrownCostGrid.CostCell(treetopsCenter, this.currentNeighborhood.Center, 0, 0, segmentationState);
            }
            if ((this.currentNeighborhood.East.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexEast, treetopCellIndexY, out Treetops? treetopsEast))
            {
                TreeCrownCostGrid.CostCell(treetopsEast, this.currentNeighborhood.East, dsmNeighborhoodOffsetEast, 0, segmentationState);
            }

            if ((this.currentNeighborhood.Southwest.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexWest, treetopCellIndexSouth, out Treetops? treetopsSouthwest))
            {
                TreeCrownCostGrid.CostCell(treetopsSouthwest, this.currentNeighborhood.Southwest, dsmNeighborhoodOffsetWest, dsmNeighborhoodOffsetSouth, segmentationState);
            }
            if ((this.currentNeighborhood.South.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexX, treetopCellIndexSouth, out Treetops? treetopsSouth))
            {
                TreeCrownCostGrid.CostCell(treetopsSouth, this.currentNeighborhood.South, 0, dsmNeighborhoodOffsetSouth, segmentationState);
            }
            if ((this.currentNeighborhood.Southeast.Count == 0) && treetopsNeighborhood.TryGetValue(treetopCellIndexEast, treetopCellIndexSouth, out Treetops? treetopsSoutheast))
            {
                TreeCrownCostGrid.CostCell(treetopsSoutheast, this.currentNeighborhood.Southeast, dsmNeighborhoodOffsetEast, dsmNeighborhoodOffsetSouth, segmentationState);
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
            if ((this.SizeX != treetopsTile.SizeX + 2) || (this.SizeY != treetopsTile.SizeY + 2))
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
