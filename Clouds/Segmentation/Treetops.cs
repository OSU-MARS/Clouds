using Mars.Clouds.GdalExtensions;
using System;
using System.Diagnostics;

namespace Mars.Clouds.Segmentation
{
    public class Treetops
    {
        public int Count { get; set; }
        public int[] ID { get; private set; } 
        public double[] X { get; private set; }
        public double[] Y { get; private set; }
        public double[] Elevation { get; private set; }
        public double[] Height { get; private set; }
        public double[] Radius { get; private set; }
        public int[,] ClassCounts { get; private set; }

        public Treetops(int treetopCapacity, int classCapacity)
        {
            this.Count = 0;
            this.ID = new int[treetopCapacity];
            this.X = new double[treetopCapacity];
            this.Y = new double[treetopCapacity];
            this.Elevation = new double[treetopCapacity];
            this.Height = new double[treetopCapacity];
            this.Radius = new double[treetopCapacity];
            this.ClassCounts = new int[treetopCapacity, classCapacity];
        }

        public int Capacity
        {
            get { return this.ID.Length; }
        }

        public void GetClassCounts(VirtualRasterNeighborhood8<byte> classificationNeighborhood)
        {
            double classificationCellSize = 0.5 * (classificationNeighborhood.Center.Transform.CellWidth + Double.Abs(classificationNeighborhood.Center.Transform.CellHeight));
            for (int treetopIndex = 0; treetopIndex < this.Count; ++treetopIndex)
            {
                (int treetopXindex, int treetopYindex) = classificationNeighborhood.Center.GetCellIndices(this.X[treetopIndex], this.Y[treetopIndex]);
                // verify treetop is on tile
                // In edge cases where a treetop lies on the tile's boundary its indices might be in an adjacent tile.
                Debug.Assert((treetopXindex >= -1) && (treetopXindex <= classificationNeighborhood.Center.XSize) && (treetopYindex >= -1) && (treetopYindex <= classificationNeighborhood.Center.YSize), "Treetop at (" + this.X[treetopIndex] + ", " + this.Y[treetopIndex] + ") is not located over center tile (extents " + classificationNeighborhood.Center.GetExtentString() + ").");
                if (classificationNeighborhood.TryGetValue(treetopXindex, treetopYindex, out byte classification))
                {
                    ++this.ClassCounts[treetopIndex, classification - 1]; // count classification of treetop
                }

                // count classification of rings up to tree radius
                int maxRadiusInCells = Int32.Min((int)(this.Radius[treetopIndex] / classificationCellSize + 0.5), Ring.Rings.Count - 1);
                for (int ringIndex = 0; ringIndex <= maxRadiusInCells; ++ringIndex)
                {
                    Ring ring = Ring.Rings[ringIndex];
                    for (int cellIndex = 0; cellIndex < ring.Count; ++cellIndex)
                    {
                        int treeXindex = treetopXindex + ring.XIndices[cellIndex];
                        int treeYindex = treetopYindex + ring.YIndices[cellIndex];
                        if (classificationNeighborhood.TryGetValue(treeXindex, treeYindex, out classification))
                        {
                            // count classification of cell in tree's assumed canopy
                            // This could, presumably, be made more accurate if trees were segmented. For now, the assumption is areas
                            // of overlap between adjacent treetop radii are ambiguous and contribute to all trees whose idealized
                            // circular crowns overlap a cell. Weights or other adjustments could be included but, for now, only a
                            // simple count is used.
                            ++this.ClassCounts[treetopIndex, classification - 1];
                        }
                    }
                }
            }
        }
    }
}
