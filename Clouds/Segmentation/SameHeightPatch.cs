using System.Collections.Generic;
using System.Numerics;

namespace Mars.Clouds.Segmentation
{
    internal class SameHeightPatch<TCell> where TCell : INumber<TCell>
    {
        private readonly List<(int rowIndex, int columnIndex, TCell elevation)> cellsInPatch;

        public int ID { get; private init; }
        public TCell Height { get; private init; }

        public SameHeightPatch(int id, TCell height, int rowIndex1, int columnIndex1, TCell elevation1, int rowIndex2, int columnIndex2, TCell elevation2)
        {
            this.cellsInPatch = new(2) { (rowIndex1, columnIndex1, elevation1), (rowIndex2, columnIndex2, elevation2) };

            this.ID = id;
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

        public (double rowIndex, double columnIndex, double elevation) GetCentroid()
        {
            // assumes all cells are the same size
            int columnIndexSum = 0;
            int rowIndexSum = 0;
            TCell elevationSum = TCell.Zero;
            for (int cellIndex = 0; cellIndex < this.cellsInPatch.Count; ++cellIndex)
            {
                (int rowIndexInPatch, int columnIndexInPatch, TCell elevation) = this.cellsInPatch[cellIndex];
                columnIndexSum += columnIndexInPatch;
                rowIndexSum += rowIndexInPatch;
                elevationSum += elevation;
            }

            double cells = this.Count;
            return (rowIndexSum / cells, columnIndexSum / cells, double.CreateChecked(elevationSum) / cells);
        }
    }
}
