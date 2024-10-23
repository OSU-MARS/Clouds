using Mars.Clouds.GdalExtensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.Segmentation
{
    public class TreeCrownCostField
    {
        private static readonly float[] AzimuthFromTreetop;
        //private static readonly float[] RadiusInCells;

        public const int CapacityRadius = 31;
        public const int CapacityXY = 2 * 31 + 1;

        private readonly float[] costField;
        private readonly bool[] searched;
        private readonly Queue<(int, int)> searchQueue;

        public int DsmMaximumX { get; private set; } // inclusive
        public int DsmMaximumY { get; private set; } // inclusive
        public int DsmMinimumX { get; private set; } // inclusive
        public int DsmMinimumY { get; private set; } // inclusive
        public int DsmOriginX { get; private set; } // index of westernmost column
        public int DsmOriginY { get; private set; } // index of northernmost row, indices always increase southwards
        public int TreeID { get; private set; }

        static TreeCrownCostField()
        {
            int cells = TreeCrownCostField.CapacityXY * TreeCrownCostField.CapacityXY;

            // populate grid of azimuth values
            TreeCrownCostField.AzimuthFromTreetop = new float[cells];
            for (int indexY = 0; indexY < TreeCrownCostField.CapacityXY; ++indexY)
            {
                int cellIndex = indexY * TreeCrownCostField.CapacityXY;
                int offsetY = indexY - TreeCrownCostField.CapacityRadius;
                for (int indexX = 0; indexX < TreeCrownCostField.CapacityXY; ++cellIndex, ++indexX)
                {
                    int offsetX = indexX - TreeCrownCostField.CapacityRadius;
                    float azimuth = -180.0F / MathF.PI * MathF.Atan2(offsetX, offsetY) + 90.0F;
                    if (azimuth < 0.0F)
                    {
                        azimuth += 360.0F;
                    }
                    TreeCrownCostField.AzimuthFromTreetop[cellIndex] = azimuth;
                }
            }

            // populate grid of radius values
            // Results in Voronoi tesselation if used as a the cost function.
            //TreeCrownCostField.RadiusInCells = new float[cells];
            //for (int indexY = 0; indexY < TreeCrownCostField.CapacityXY; ++indexY)
            //{
            //    int cellIndex = indexY * TreeCrownCostField.CapacityXY;
            //    int offsetY = indexY - TreeCrownCostField.CapacityRadius;
            //    int offsetYsquared = offsetY * offsetY;
            //    for (int indexX = 0; indexX < TreeCrownCostField.CapacityXY; ++cellIndex, ++indexX)
            //    {
            //        int offsetX = indexX - TreeCrownCostField.CapacityRadius;
            //        float radiusInCells = MathF.Sqrt(offsetX * offsetX + offsetYsquared);
            //        TreeCrownCostField.RadiusInCells[cellIndex] = radiusInCells;
            //    }
            //}
        }

        public TreeCrownCostField()
        {
            int cells = TreeCrownCostField.CapacityXY * TreeCrownCostField.CapacityXY;
            this.costField = GC.AllocateUninitializedArray<float>(cells);
            this.searched = GC.AllocateUninitializedArray<bool>(cells);
            this.searchQueue = new(6 * TreeCrownCostField.CapacityXY); // six rows is probably adequate to avoid reallocations (could check if CapacityXY is less than six, but that's unlikely)

            this.DsmMaximumX = -1;
            this.DsmMaximumY = -1;
            this.DsmMinimumX = -1;
            this.DsmMinimumY = -1;
            this.DsmOriginX = -1;
            this.DsmOriginY = -1;
            this.TreeID = -1;
        }

        //public float this[int dsmIndexX, int dsmIndexY]
        //{
        //    get { return this.costField[this.DsmToCellIndex(dsmIndexX, dsmIndexY)]; }
        //    set { this.costField[this.DsmToCellIndex(dsmIndexX, dsmIndexY)] = value; }
        //}

        /// <summary>
        /// Check if cost field's axis aligned bounding box overlaps with another bounding box.
        /// </summary>
        /// <param name="dsmMinIndexX">Inclusive minimum x index of bounding box on the digital surface model.</param>
        /// <param name="dsmMaxIndexX">Exclusive maximum x index of bounding box on the digital surface model.</param>
        /// <param name="dsmMinIndexY">Inclusive minimum y index of bounding box on the digital surface model.</param>
        /// <param name="dsmMaxIndexY">Exclusive maximum y index of bounding box on the digital surface model.</param>
        /// <returns></returns>
        public bool Intersects(int dsmMinIndexX, int dsmMaxIndexX, int dsmMinIndexY, int dsmMaxIndexY)
        {
            // two axis aligned bounding boxes overlap when their x and y extents both overlap
            // In floating point, the minimum test for overlap on an axis is the symmetric check is usually
            //   (min1 <= max2) && (max1 >= min2) <=> (min2 <= max1) && (max2 >= min1) = (max1 >= min2) && (min1 <= max2) = (min1 <= max2) && (max1 >= min2)
            // as neither condition is sufficient on its own. Use of <= and >= remains unchanged with integers but, with exclusive
            // maxima indices, the test becomes
            //   (min1 < max2) && (max1 > min2)
            // In this case the comparison is between inclusive minima indices and maxima which are either inclusive (this.)
            // or exclusive (passed arguments). The operators used therefore depend on which index is on which side of the
            // comparison.
            return (this.DsmMinimumX < dsmMaxIndexX) && (this.DsmMaximumX >= dsmMinIndexX) &&
                   (this.DsmMinimumY < dsmMaxIndexY) && (this.DsmMaximumY >= dsmMinIndexY);
        }

        /// <summary>
        /// Calculate a new treetop's cost field.
        /// </summary>
        /// <param name="treetopZ">Height of treetop as a DSM elevation.</param>
        /// <remarks>
        /// For treetops designated from a single local maxima the treetop elevation will be the same as the DSM's z value for the
        /// cell. For treetops from merge points the treetop may be higher than the DSM or, potentially, lower. Cost field calculation 
        /// uses whichever is higher.
        /// </remarks>
        public void MoveToTreetopAndRecalculate(int dsmTreetopIndexX, int dsmTreetopIndexY, int treetopID, float treetopZ, TreeCrownSegmentationState segmentationState)
        {
            if ((segmentationState.DsmNeighborhood == null) || (segmentationState.ChmNeighborhood == null) || (segmentationState.SlopeNeighborhood == null) || (segmentationState.AspectNeighborhood == null))
            {
                throw new ArgumentOutOfRangeException(nameof(segmentationState), "Segmentation state is missing DSM, CHM, slope, or aspect neighborhood.");
            }
            RasterBand<float> dsm = segmentationState.DsmNeighborhood.Center;
            RasterBand<float> chm = segmentationState.ChmNeighborhood.Center;
            RasterBand<float> slope = segmentationState.SlopeNeighborhood.Center;
            RasterBand<float> aspect = segmentationState.AspectNeighborhood.Center;

            if (-dsm.Transform.CellHeight != dsm.Transform.CellWidth)
            {
                throw new NotSupportedException("Digital surface models with rectangular cells or positive cell heights are not currently supported.");
            }
            if (dsm.TryGetValue(dsmTreetopIndexX, dsmTreetopIndexY, out float dsmTreetopZ) == false)
            {
                // having a DSM value at the treetop is not strictly necessary but is checked for consistency
                // DSM values are required for all other pixels in the crown.
                throw new NotSupportedException("Digital surface model lacks a value for treetop " + treetopID + " at (" + dsmTreetopIndexX + ", " + dsmTreetopIndexY + ").");
            }
            if (chm.TryGetValue(dsmTreetopIndexX, dsmTreetopIndexY, out float chmZ) == false)
            {
                throw new NotSupportedException("Digital surface model cell at (" + dsmTreetopIndexX + ", " + dsmTreetopIndexY + ") has a DSM elevation but lacks a CHM height.");
            }

            if (dsmTreetopZ > treetopZ)
            {
                treetopZ = dsmTreetopZ;
            }
            float minimumCrownDsmZ = treetopZ - segmentationState.MaximumCrownRatio * chmZ; // use treetop height rather than DSM height

            this.DsmOriginX = dsmTreetopIndexX - TreeCrownCostField.CapacityRadius;
            this.DsmOriginY = dsmTreetopIndexY - TreeCrownCostField.CapacityRadius;
            this.TreeID = treetopID;

            Array.Fill(this.costField, Single.PositiveInfinity); // by convention, Falcão et al. 2004 (https://doi.org/10.1109/TPAMI.2004.1261076)
            Array.Fill(this.searched, false);
            Debug.Assert(this.searchQueue.Count == 0);

            int centerIndexX = TreeCrownCostField.CapacityRadius;
            int centerIndexY = TreeCrownCostField.CapacityRadius;
            int centerIndex = TreeCrownCostField.ToCellIndex(centerIndexX, centerIndexY);
            this.costField[centerIndex] = 0.0F; // cost at marker is defined to be zero (could also use Single.NegativeInfinity), regardless of canopy height
            this.searched[centerIndex] = true;

            // grow crown region
            // Growth breadth first search as Queue<T> is FIFO.
            float dsmCellSize = (float)dsm.Transform.CellWidth;
            this.searchQueue.Enqueue((centerIndexX, centerIndexY));
            while (this.searchQueue.Count > 0)
            {
                (int searchCellIndexX, int searchCellIndexY) = this.searchQueue.Dequeue();
                // this.SearchNeighborhood4(searchCellIndexX, searchCellIndexY, dsm, dsmCellSize, treetopZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
                this.SearchNeighborhood8(searchCellIndexX, searchCellIndexY, dsm, dsmCellSize, treetopZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
            }

            // TODO: check for asymmetry and recenter cost function?
            // TODO: revise treetop position?

            // find bounding box
            // Bounding box could be found during growing but obtaining bounds after growth appears likely more efficient.
            // Current implementation is data layout aligned, and thus prefetch friendly, but intelligent quadrant search might be
            // more efficient.
            int minimumIndexX = TreeCrownCostField.CapacityRadius;
            int minimumIndexY = TreeCrownCostField.CapacityRadius;
            int maximumIndexX = TreeCrownCostField.CapacityRadius;
            int maximumIndexY = TreeCrownCostField.CapacityRadius;
            for (int indexY = 0; indexY < TreeCrownCostField.CapacityXY; ++indexY)
            {
                int cellIndex = indexY * TreeCrownCostField.CapacityXY;
                for (int indexX = 0; indexX < TreeCrownCostField.CapacityXY; ++cellIndex, ++indexX)
                {
                    if (this.costField[cellIndex] == Single.PositiveInfinity)
                    {
                        continue;
                    }

                    if (indexX < minimumIndexX)
                    {
                        minimumIndexX = indexX;
                    }
                    else if (indexX > maximumIndexX)
                    {
                        maximumIndexX = indexX;
                    }
                    if (indexY < minimumIndexY)
                    {
                        minimumIndexY = indexY;
                    }
                    else if (indexY > maximumIndexY)
                    {
                        maximumIndexY = indexY;
                    }
                }
            }

            Debug.Assert((0 <= minimumIndexX) && (minimumIndexX <= maximumIndexX) && (maximumIndexX < TreeCrownCostField.CapacityXY) &&
                         (0 <= minimumIndexY) && (minimumIndexY <= maximumIndexY) && (maximumIndexY < TreeCrownCostField.CapacityXY));
            this.DsmMaximumX = this.DsmOriginX + maximumIndexX;
            this.DsmMaximumY = this.DsmOriginY + maximumIndexY;
            this.DsmMinimumX = this.DsmOriginX + minimumIndexX;
            this.DsmMinimumY = this.DsmOriginY + minimumIndexY;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SearchCell(int cellIndexX, int cellIndexY, float baseCostInCrsUnits, float arcLengthInCrsUnits, RasterBand<float> dsm, int dsmIndexX, int dsmIndexY, float treetopDsmZ, float minimumCrownDsmZ, RasterBand<float> chm, RasterBand<float> slope, RasterBand<float> aspect, TreeCrownSegmentationState segmentationState)
        {
            int cellIndex = TreeCrownCostField.ToCellIndex(cellIndexX, cellIndexY);
            if (this.searched[cellIndex] == false)
            {
                // TODO: profile value of on tile branches
                bool cellInTile = (0 <= dsmIndexX) && (dsmIndexX < dsm.SizeX) &&
                                  (0 <= dsmIndexY) && (dsmIndexY < dsm.SizeY);
                float dsmZ;
                float chmHeight = Single.NegativeInfinity;
                float dsmSlope = Single.NaN;
                float dsmAspect = Single.NaN;
                bool hasDsmChmZ = false;
                bool hasSlopeAspect = false;
                if (cellInTile)
                {
                    if (dsm.TryGetValue(dsmIndexX, dsmIndexY, out dsmZ))
                    {
                        if (chm.TryGetValue(dsmIndexX, dsmIndexY, out chmHeight))
                        {
                            hasDsmChmZ = true;
                            if (slope.TryGetValue(dsmIndexX, dsmIndexY, out dsmSlope))
                            {
                                hasSlopeAspect = aspect.TryGetValue(dsmIndexX, dsmIndexY, out dsmAspect);
                            }
                        }
                    }
                }
                else
                {
                    Debug.Assert((segmentationState.DsmNeighborhood != null) && (segmentationState.ChmNeighborhood != null) && (segmentationState.SlopeNeighborhood != null) && (segmentationState.AspectNeighborhood != null));
                    if (segmentationState.DsmNeighborhood.TryGetValue(dsmIndexX, dsmIndexY, out dsmZ))
                    {
                        if (segmentationState.ChmNeighborhood.TryGetValue(dsmIndexX, dsmIndexY, out chmHeight))
                        {
                            hasDsmChmZ = true;
                            if (segmentationState.SlopeNeighborhood.TryGetValue(dsmIndexX, dsmIndexY, out dsmSlope))
                            {
                                hasSlopeAspect = segmentationState.AspectNeighborhood.TryGetValue(dsmIndexX, dsmIndexY, out dsmAspect);
                            }
                        }
                    }
                }

                this.searched[cellIndex] = true;
                if ((hasDsmChmZ == false) || (chmHeight < segmentationState.MinimumHeightInCrsUnits) || (dsmZ < minimumCrownDsmZ))
                {
                    // no crown connectivity through 
                    // - cells without DSM and CHM values: assume no hits means no crown
                    // - near-ground cells: assume little to no crown extension along ground (usually on uphill side of tree)
                    // - cells below crown height limit: reduce inclusion of shorter trees, shrubs, and herbaceous layer
                    return;
                }

                // outwards aspect => downwards slope: reduce arc cost as |relative azimuth| declines from 90° to 0°
                // inwards aspect => upwards slope: increase arc cost as |relative azimuth| increases from 90° to 180°
                // TODO: bound maximum connected cost by height of tree?
                // TODO: constrain connectivity to not go too far into adjacent trees' crowns?
                float arcCostMultiplier = 1.0F;
                if (hasSlopeAspect)
                {
                    float outwardAzimuth = TreeCrownCostField.AzimuthFromTreetop[cellIndex];
                    float relativeAzimuth = dsmAspect - outwardAzimuth;
                    if (relativeAzimuth < 0.0F)
                    {
                        relativeAzimuth = -relativeAzimuth;
                    }
                    if (relativeAzimuth <= 90.0F)
                    {
                        //arcCostMultiplier -= dsmSlope / 90.0F * (90.0F - relativeAzimuth) / 90.0F;
                    }
                    else
                    {
                        //arcCostMultiplier += dsmSlope / 90.0F * (relativeAzimuth - 90.0F) / 90.0F;
                    }
                }

                if (dsmZ > treetopDsmZ)
                {
                    //arcCostMultiplier += segmentationState.AboveTopCostScaleFactor * (dsmZ - treetopDsmZ) / arcLengthInCrsUnits;
                }

                this.costField[cellIndex] = baseCostInCrsUnits + arcCostMultiplier * arcLengthInCrsUnits;
                this.searchQueue.Enqueue((cellIndexX, cellIndexY));
            }
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //private void SearchNeighborhood4(int cellIndexX, int cellIndexY, RasterBand<float> dsm, float dsmCellSize, float treetopDsmZ, float minimumCrownDsmZ, RasterBand<float> chm, RasterBand<float> slope, RasterBand<float> aspect, TreeCrownSegmentationState segmentationState)
        //{
        //    int dsmIndexX = this.DsmOriginX + cellIndexX;
        //    int dsmIndexY = this.DsmOriginY + cellIndexY;
        //    float centerCostInCrsUnits = this.costField[TreeCrownCostField.ToCellIndex(cellIndexX, cellIndexY)];

        //    // west neighbor
        //    if (cellIndexX > 0)
        //    {
        //        this.SearchCell(cellIndexX - 1, cellIndexY, centerCostInCrsUnits, dsmCellSize, dsm, dsmIndexX - 1, dsmIndexY, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
        //    }
        //    // east neighbor
        //    if (cellIndexX < TreeCrownCostField.CapacityXY - 1)
        //    {
        //        this.SearchCell(cellIndexX + 1, cellIndexY, centerCostInCrsUnits, dsmCellSize, dsm, dsmIndexX + 1, dsmIndexY, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
        //    }
        //    // north neighbor
        //    if (cellIndexY > 0)
        //    {
        //        this.SearchCell(cellIndexX, cellIndexY - 1, centerCostInCrsUnits, dsmCellSize, dsm, dsmIndexX, dsmIndexY - 1, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
        //    }
        //    // south neighbor
        //    if (cellIndexY < TreeCrownCostField.CapacityXY - 1)
        //    {
        //        this.SearchCell(cellIndexX, cellIndexY + 1, centerCostInCrsUnits, dsmCellSize, dsm, dsmIndexX, dsmIndexY + 1, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
        //    }
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SearchNeighborhood8(int cellIndexX, int cellIndexY, RasterBand<float> dsm, float dsmCellSize, float treetopDsmZ, float minimumCrownDsmZ, RasterBand<float> chm, RasterBand<float> slope, RasterBand<float> aspect, TreeCrownSegmentationState segmentationState)
        {
            int dsmIndexX = this.DsmOriginX + cellIndexX;
            int dsmIndexY = this.DsmOriginY + cellIndexY;
            float centerCostInCrsUnits = this.costField[TreeCrownCostField.ToCellIndex(cellIndexX, cellIndexY)];
            float diagonalArcLength = Constant.Sqrt2 * dsmCellSize;

            // unlike SearchNeighborhood4(), varying arc length makes traversal order significant
            // Two axis-aligned steps always create a different path costs than one or two diagonal steps from the same starting
            // location to the same ending location.
            // west neighbor
            if (cellIndexX > 0)
            {
                this.SearchCell(cellIndexX - 1, cellIndexY, centerCostInCrsUnits, dsmCellSize, dsm, dsmIndexX - 1, dsmIndexY, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
            }
            // east neighbor
            if (cellIndexX < TreeCrownCostField.CapacityXY - 1)
            {
                this.SearchCell(cellIndexX + 1, cellIndexY, centerCostInCrsUnits, dsmCellSize, dsm, dsmIndexX + 1, dsmIndexY, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
            }
            // north neighbor
            if (cellIndexY > 0)
            {
                this.SearchCell(cellIndexX, cellIndexY - 1, centerCostInCrsUnits, dsmCellSize, dsm, dsmIndexX, dsmIndexY - 1, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
            }
            // south neighbor
            if (cellIndexY < TreeCrownCostField.CapacityXY - 1)
            {
                this.SearchCell(cellIndexX, cellIndexY + 1, centerCostInCrsUnits, dsmCellSize, dsm, dsmIndexX, dsmIndexY + 1, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
            }

            // northwest neighbor
            if ((cellIndexX > 0) && (cellIndexY > 0))
            {
                this.SearchCell(cellIndexX - 1, cellIndexY - 1, centerCostInCrsUnits, diagonalArcLength, dsm, dsmIndexX - 1, dsmIndexY, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
            }
            // northeast neighbor
            if ((cellIndexX < TreeCrownCostField.CapacityXY - 1) && (cellIndexY > 0))
            {
                this.SearchCell(cellIndexX + 1, cellIndexY - 1, centerCostInCrsUnits, diagonalArcLength, dsm, dsmIndexX - 1, dsmIndexY, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
            }
            // southeast neighbor
            if ((cellIndexX < TreeCrownCostField.CapacityXY - 1) && (cellIndexY < TreeCrownCostField.CapacityXY - 1))
            {
                this.SearchCell(cellIndexX + 1, cellIndexY + 1, centerCostInCrsUnits, diagonalArcLength, dsm, dsmIndexX - 1, dsmIndexY, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
            }
            // southwest neighbor
            if ((cellIndexX > 0) && (cellIndexY < TreeCrownCostField.CapacityXY - 1))
            {
                this.SearchCell(cellIndexX - 1, cellIndexY + 1, centerCostInCrsUnits, diagonalArcLength, dsm, dsmIndexX - 1, dsmIndexY, treetopDsmZ, minimumCrownDsmZ, chm, slope, aspect, segmentationState);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ToCellIndex(int fieldIndexX, int fieldIndexY)
        {
            return fieldIndexX + fieldIndexY * TreeCrownCostField.CapacityXY;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(int dsmIndexX, int dsmIndexY, out float cost)
        {
            // check if DSM coordinates lie within cost field
            // Misses are routine. See also off grid remarks in Grid.ToCellIndex().
            int cellIndexX = dsmIndexX - this.DsmOriginX;
            if ((cellIndexX < 0) || (cellIndexX >= TreeCrownCostField.CapacityXY))
            {
                cost = default;
                return false;
            }

            int cellIndexY = dsmIndexY - this.DsmOriginY;
            if ((cellIndexY < 0) || (cellIndexY >= TreeCrownCostField.CapacityXY))
            {
                cost = default;
                return false;
            }

            // check if cost field has a value at this position
            int cellIndex = TreeCrownCostField.ToCellIndex(cellIndexX, cellIndexY);
            cost = this.costField[cellIndex];
            return cost != Single.PositiveInfinity;
        }
    }
}
