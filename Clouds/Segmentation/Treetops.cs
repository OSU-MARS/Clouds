using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.Segmentation
{
    public class Treetops
    {
        public int Count { get; set; }
        public int[] ID { get; private set; }
        public double[] X { get; private set; }
        public int[] XIndex { get; private set; }
        public double[] Y { get; private set; }
        public int[] YIndex { get; private set; }
        public double[] Elevation { get; private set; }
        public double[] Height { get; private set; }
        public double[] Radius { get; private set; }
        public int[,] ClassCounts { get; private set; }

        public Treetops(int treetopCapacity, bool xyIndices, int classCapacity)
        {
            this.Count = 0;
            this.ID = new int[treetopCapacity];
            this.X = new double[treetopCapacity];
            this.Y = new double[treetopCapacity];
            this.Elevation = new double[treetopCapacity];
            this.Height = new double[treetopCapacity];
            this.Radius = new double[treetopCapacity];

            if (xyIndices)
            {
                this.XIndex = new int[treetopCapacity];
                this.YIndex = new int[treetopCapacity];
            }
            else
            {
                this.XIndex = [];
                this.YIndex = [];
            }

            if (classCapacity > 0)
            {
                this.ClassCounts = new int[treetopCapacity, classCapacity];
            }
            else
            {
                this.ClassCounts = new int[0, 0];
            }
        }

        public int Capacity
        {
            get { return this.ID.Length; }
        }

        public void Clear()
        {
            this.Count = 0;
        }

        public void Extend(int newCapacity)
        {
            this.ID = this.ID.Extend(newCapacity);
            this.X = this.X.Extend(newCapacity);
            this.Y = this.Y.Extend(newCapacity);
            this.Elevation = this.Elevation.Extend(newCapacity);
            this.Height = this.Height.Extend(newCapacity);
            this.Radius = this.Radius.Extend(newCapacity);

            if (this.XIndex.Length > 0)
            {
                this.XIndex = this.XIndex.Extend(newCapacity);
            }
            if (this.YIndex.Length > 0)
            {
                this.YIndex = this.YIndex.Extend(newCapacity);
            }

            if (this.ClassCounts.Length > 0)
            {
                this.ClassCounts = this.ClassCounts.Extend(newCapacity);
            }
        }

        public void GetClassCounts(RasterNeighborhood8<int> crownNeighborhood, RasterNeighborhood8<byte> classificationNeighborhood)
        {
            Queue<(int, int)> searchQueue = new(6 * TreeCrownCostField.CapacityXY);
            Span<int> counts = stackalloc int[(int)LandCoverClassification.MaxValue];
            for (int treetopIndex = 0; treetopIndex < this.Count; ++treetopIndex)
            {
                int treeID = this.ID[treetopIndex];
                (int treetopIndexX, int treetopIndexY) = classificationNeighborhood.Center.ToGridIndices(this.X[treetopIndex], this.Y[treetopIndex]);
                if ((crownNeighborhood.TryGetValue(treetopIndexX, treetopIndexY, out int crownID) == false) || (crownID != treeID))
                {
                    throw new InvalidOperationException("Tree " + treetopIndex + " at indices (" + treetopIndexX + ", " + treetopIndexY + ") has ID " + treeID + " but crown raster has ID " + crownID + " at that cell.");
                }

                searchQueue.Enqueue((treetopIndexX, treetopIndexY));
                while (searchQueue.Count > 0)
                {
                    (int searchCellIndexX, int searchCellIndexY) = searchQueue.Dequeue();
                    Treetops.SearchNeighborhood(searchCellIndexX, searchCellIndexY, treeID, crownNeighborhood, classificationNeighborhood, counts, searchQueue);
                }

                for (int classIndex = 0; classIndex < counts.Length; ++classIndex)
                {
                    this.ClassCounts[treetopIndex, classIndex] = counts[classIndex];
                    counts[classIndex] = 0;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SearchNeighborhood(int cellIndexX, int cellIndexY, int treeID, RasterNeighborhood8<int> crownNeighborhood, RasterNeighborhood8<byte> classificationNeighborhood, Span<int> counts, Queue<(int, int)> searchQueue)
        {
            // west neighbor
            int westIndex = cellIndexX - 1;
            if (crownNeighborhood.TryGetValue(westIndex, cellIndexY, out int westCrownID) && (westCrownID == treeID))
            {
                if (classificationNeighborhood.TryGetValue(westIndex, cellIndexY, out byte classification) == false)
                {
                    throw new InvalidOperationException("Tree " + treeID + "'s canopy extends to (" + westIndex + ", " + cellIndexY + ") but a classification is not available at this position.");
                }

                ++counts[classification];
                searchQueue.Enqueue((westIndex, cellIndexY));
            }

            // east neighbor
            int eastIndex = cellIndexX + 1;
            if (crownNeighborhood.TryGetValue(eastIndex, cellIndexY, out int eastCrownID) && (eastCrownID == treeID))
            {
                if (classificationNeighborhood.TryGetValue(eastIndex, cellIndexY, out byte classification) == false)
                {
                    throw new InvalidOperationException("Tree " + treeID + "'s canopy extends to (" + eastIndex + ", " + cellIndexY + ") but a classification is not available at this position.");
                }

                ++counts[classification];
                searchQueue.Enqueue((eastIndex, cellIndexY));
            }

            // north neighbor
            int northIndex = cellIndexY - 1;
            if (crownNeighborhood.TryGetValue(cellIndexX, northIndex, out int northCrownID) && (northCrownID == treeID))
            {
                if (classificationNeighborhood.TryGetValue(cellIndexX, northIndex, out byte classification) == false)
                {
                    throw new InvalidOperationException("Tree " + treeID + "'s canopy extends to (" + cellIndexX + ", " + northIndex + ") but a classification is not available at this position.");
                }

                ++counts[classification];
                searchQueue.Enqueue((cellIndexX, northIndex));
            }

            // south neighbor
            int southIndex = cellIndexY + 1;
            if (crownNeighborhood.TryGetValue(cellIndexX, southIndex, out int southCrownID) && (southCrownID == treeID))
            {
                if (classificationNeighborhood.TryGetValue(cellIndexX, southIndex, out byte classification) == false)
                {
                    throw new InvalidOperationException("Tree " + treeID + "'s canopy extends to (" + cellIndexX + ", " + southIndex + ") but a classification is not available at this position.");
                }

                ++counts[classification];
                searchQueue.Enqueue((cellIndexX, southIndex));
            }
        }
    }
}
