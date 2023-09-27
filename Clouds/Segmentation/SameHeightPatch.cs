using System.Collections.Generic;
using System.Numerics;

namespace Mars.Clouds.Segmentation
{
    internal class SameHeightPatch<TCell> where TCell : INumber<TCell>
    {
        private readonly List<(int rowIndex, int columnIndex, TCell elevation)> cellsInPatch;

        public int ID { get; set; }
        public TCell Height { get; private init; }

        public SameHeightPatch(TCell height, int rowIndex1, int columnIndex1, TCell elevation1, int rowIndex2, int columnIndex2, TCell elevation2)
        {
            this.cellsInPatch = new(2) { (rowIndex1, columnIndex1, elevation1), (rowIndex2, columnIndex2, elevation2) };

            this.ID = -1;
            this.Height = height;
        }

        public int Count
        {
            get { return this.cellsInPatch.Count; }
        }

        public void Add(int rowIndex, int columnIndex, TCell elevation)
        {
            this.cellsInPatch.Add((rowIndex, columnIndex, elevation));
        }

        public bool Contains(int rowIndex, int columnIndex)
        {
            for (int cellIndex = 0; cellIndex < this.cellsInPatch.Count; ++cellIndex)
            {
                (int rowIndexInPatch, int columnIndexInPatch, TCell _) = this.cellsInPatch[cellIndex];
                if ((rowIndexInPatch == rowIndex) && (columnIndexInPatch == columnIndex))
                {
                    return true;
                }
            }

            return false;
        }

        public (double rowIndexFractional, double columnIndexFractional, TCell elevation) GetCentroid()
        {
            // assumes all cells are the same size
            int columnIndexSum = 0;
            int rowIndexSum = 0;
            TCell elevationSum = TCell.Zero;
            int cellsWithElevation = 0;
            for (int cellIndex = 0; cellIndex < this.cellsInPatch.Count; ++cellIndex)
            {
                (int rowIndexInPatch, int columnIndexInPatch, TCell elevation) = this.cellsInPatch[cellIndex];
                columnIndexSum += columnIndexInPatch;
                rowIndexSum += rowIndexInPatch;
                if (TCell.IsNaN(elevation) == false)
                {
                    ++cellsWithElevation;
                    elevationSum += elevation;
                }
            }

            // xy location of centroid is the average cell index plus half of a cell
            // Since a centroid is in the middle of the cell its index is (cellRowIndex + 0.5, cellColumnIndex + 0.5).
            double cells = this.Count;
            return (rowIndexSum / cells + 0.5F, columnIndexSum / cells + 0.5F, elevationSum / TCell.CreateChecked(cellsWithElevation));
        }
    }
}
