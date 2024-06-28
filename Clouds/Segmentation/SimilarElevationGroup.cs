using System;
using System.Collections.Generic;
using System.Numerics;

namespace Mars.Clouds.Segmentation
{
    internal class SimilarElevationGroup<TCell> where TCell : INumber<TCell>
    {
        private readonly List<(int xIndex, int yIndex, TCell elevation)> cellsInPatch;
        private readonly int radiusFromSingleCell;

        public int ID { get; set; }
        public TCell Height { get; private init; }

        public SimilarElevationGroup(TCell height, int yIndex1, int xIndex1, TCell elevation1, int yIndex2, int xIndex2, TCell elevation2, int radiusInCells)
        {
            this.cellsInPatch = [ (xIndex1, yIndex1, elevation1), (xIndex2, yIndex2, elevation2) ];
            this.radiusFromSingleCell = radiusInCells;

            this.ID = -1;
            this.Height = height;
        }

        public int Count
        {
            get { return this.cellsInPatch.Count; }
        }

        public void Add(int xIndex, int yIndex, TCell elevation)
        {
            this.cellsInPatch.Add((xIndex, yIndex, elevation));
        }

        public bool Contains(int xIndex, int yIndex)
        {
            for (int cellIndex = 0; cellIndex < this.cellsInPatch.Count; ++cellIndex)
            {
                (int xIndexInPatch, int yIndexInPatch, TCell _) = this.cellsInPatch[cellIndex];
                if ((xIndexInPatch == xIndex) && (yIndexInPatch == yIndex))
                {
                    return true;
                }
            }

            return false;
        }

        public (double xIndexFractional, double yIndexFractional, TCell elevation, float radiusInCells) GetCentroid()
        {
            // assumes all cells are the same size
            int xIndexSum = 0;
            int yIndexSum = 0;
            TCell elevationSum = TCell.Zero;
            int cellsWithElevation = 0;
            int minXindex = Int32.MaxValue;
            int maxXindex = Int32.MinValue;
            int minYindex = Int32.MaxValue;
            int maxYindex = Int32.MinValue;
            for (int cellIndex = 0; cellIndex < this.cellsInPatch.Count; ++cellIndex)
            {
                (int xIndexInPatch, int yIndexInPatch, TCell elevation) = this.cellsInPatch[cellIndex];
                xIndexSum += xIndexInPatch;
                yIndexSum += yIndexInPatch;
                if (TCell.IsNaN(elevation) == false)
                {
                    ++cellsWithElevation;
                    elevationSum += elevation;
                }
                if (xIndexInPatch < minXindex)
                {
                    minXindex = xIndexInPatch;
                }
                if (xIndexInPatch > maxXindex)
                {
                    maxXindex = xIndexInPatch;                    
                }
                if (yIndexInPatch < minYindex)
                {
                    minYindex = yIndexInPatch;
                }
                if (yIndexInPatch > maxYindex)
                {
                    maxYindex = yIndexInPatch;
                }
            }

            int xRangeInCells = maxXindex - minXindex;
            int yRangeInCells = maxXindex - minXindex;
            float radiusInCells = this.radiusFromSingleCell + 0.5F * Single.Sqrt(xRangeInCells * xRangeInCells + yRangeInCells * yRangeInCells);

            // xy location of centroid is the average cell index plus half of a cell
            // Since a centroid is in the middle of the cell its index is (cellRowIndex + 0.5, cellColumnIndex + 0.5).
            double cells = this.Count;
            return (xIndexSum / cells + 0.5F, yIndexSum / cells + 0.5F, elevationSum / TCell.CreateChecked(cellsWithElevation), radiusInCells);
        }
    }
}
