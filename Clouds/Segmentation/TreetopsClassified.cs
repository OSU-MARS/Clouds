using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mars.Clouds.Segmentation
{
    public class TreetopsClassified : Treetops
    {
        private static readonly int LandCoverClasses = Enum.GetValues<LandCoverClassification>().Length;

        public int[,] ClassCounts { get; private set; }

        public TreetopsClassified(int treetopCapacity)
            : base(treetopCapacity)
        {
            this.ClassCounts = new int[treetopCapacity, TreetopsClassified.LandCoverClasses];
        }

        public void GetClassCounts(RasterNeighborhood8<int> crownNeighborhood, RasterNeighborhood8<byte> classificationNeighborhood)
        {
            Span<int> counts = stackalloc int[TreetopsClassified.LandCoverClasses];
            bool[] isCellQueued = new bool[TreeCrownCostField.Cells];
            Queue<(int, int)> searchQueue = new(6 * TreeCrownCostField.CapacityXY);
            for (int treetopIndex = 0; treetopIndex < this.Count; ++treetopIndex)
            {
                int treeID = this.ID[treetopIndex];
                (int treetopIndexX, int treetopIndexY) = classificationNeighborhood.Center.ToGridIndices(this.X[treetopIndex], this.Y[treetopIndex]);
                if ((crownNeighborhood.TryGetValue(treetopIndexX, treetopIndexY, out int crownID) == false) || (crownID != treeID))
                {
                    throw new InvalidOperationException($"Tree {treetopIndex} at indices ({treetopIndexX}, {treetopIndexY}) has ID {treeID} but crown raster has ID {crownID} at that cell.");
                }

                // count classification of treetop cell
                if (classificationNeighborhood.TryGetValue(treetopIndexX, treetopIndexY, out byte classification) == false)
                {
                    // See remarks on unclassified and no data in SearchNeighborhood().
                    classification = (byte)LandCoverClassification.Unclassified;
                }
                ++counts[classification];

                // accumulate classifications of rook connected crown cells
                searchQueue.Enqueue((treetopIndexX, treetopIndexY));
                isCellQueued[TreeCrownCostField.ToCellIndex(TreeCrownCostField.CapacityRadius, TreeCrownCostField.CapacityRadius)] = true;
                while (searchQueue.Count > 0)
                {
                    (int searchCellIndexX, int searchCellIndexY) = searchQueue.Dequeue();
                    TreetopsClassified.SearchNeighborhood(treetopIndexX, treetopIndexY, searchCellIndexX, searchCellIndexY, treeID, crownNeighborhood, classificationNeighborhood, counts, searchQueue, isCellQueued);
                    Debug.Assert(searchQueue.Count < TreeCrownCostField.Cells, "Treetop crown traversal quequed more cells for search than are present within a tree crown cost field.");
                }

                counts.CopyTo(MemoryMarshal.CreateSpan(ref this.ClassCounts[treetopIndex, 0], TreetopsClassified.LandCoverClasses));

                counts.Clear();
                Array.Clear(isCellQueued);
            }
        }

        public override void Extend(int newCapacity)
        {
            base.Extend(newCapacity);

            if (this.ClassCounts.Length > 0)
            {
                this.ClassCounts = this.ClassCounts.Extend(newCapacity);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SearchNeighborhood(int treetopIndexX, int treetopIndexY, int searchCellIndexX, int searchCellIndexY, int treeID, RasterNeighborhood8<int> crownNeighborhood, RasterNeighborhood8<byte> classificationNeighborhood, Span<int> counts, Queue<(int, int)> searchQueue, bool[] cellQueuedOrNoData)
        {
            int fieldIndexX = searchCellIndexX - treetopIndexX + TreeCrownCostField.CapacityRadius;
            int fieldIndexY = searchCellIndexY - treetopIndexY + TreeCrownCostField.CapacityRadius;

            // west neighbor
            if (fieldIndexX > 0)
            {
                int fieldIndexWest = TreeCrownCostField.ToCellIndex(fieldIndexX - 1, fieldIndexY);
                if (cellQueuedOrNoData[fieldIndexWest] == false)
                {
                    int westCellIndex = searchCellIndexX - 1;
                    if (crownNeighborhood.TryGetValue(westCellIndex, searchCellIndexY, out int westCrownID) && (westCrownID == treeID))
                    {
                        if (classificationNeighborhood.TryGetValue(westCellIndex, searchCellIndexY, out byte classification) == false)
                        {
                            // default value of LandCoverClassification is Unclassified, which is currently treated as interchangeable with no data
                            // These two cases can be distinguished if needed by checking the classification virtual raster's no data value.
                            // For now, any no data return is aliased to Unclassified.
                            classification = (byte)LandCoverClassification.Unclassified;
                        }
                        ++counts[classification];
                        searchQueue.Enqueue((westCellIndex, searchCellIndexY));
                    }

                    cellQueuedOrNoData[fieldIndexWest] = true;
                }
            }

            // east neighbor
            if (fieldIndexX < TreeCrownCostField.CapacityXY - 1)
            {
                int fieldIndexEast = TreeCrownCostField.ToCellIndex(fieldIndexX + 1, fieldIndexY);
                if (cellQueuedOrNoData[fieldIndexEast] == false)
                {
                    int eastCellIndex = searchCellIndexX + 1;
                    if (crownNeighborhood.TryGetValue(eastCellIndex, searchCellIndexY, out int eastCrownID) && (eastCrownID == treeID))
                    {
                        if (classificationNeighborhood.TryGetValue(eastCellIndex, searchCellIndexY, out byte classification) == false)
                        {
                            classification = (byte)LandCoverClassification.Unclassified;
                        }

                        ++counts[classification];
                        searchQueue.Enqueue((eastCellIndex, searchCellIndexY));
                    }

                    cellQueuedOrNoData[fieldIndexEast] = true;
                }
            }

            // north neighbor
            if (fieldIndexY > 0)
            {
                int fieldIndexNorth = TreeCrownCostField.ToCellIndex(fieldIndexX, fieldIndexY - 1);
                if (cellQueuedOrNoData[fieldIndexNorth] == false)
                {
                    int northCellIndex = searchCellIndexY - 1;
                    if (crownNeighborhood.TryGetValue(searchCellIndexX, northCellIndex, out int northCrownID) && (northCrownID == treeID))
                    {
                        if (classificationNeighborhood.TryGetValue(searchCellIndexX, northCellIndex, out byte classification) == false)
                        {
                            classification = (byte)LandCoverClassification.Unclassified;
                        }

                        ++counts[classification];
                        searchQueue.Enqueue((searchCellIndexX, northCellIndex));
                    }

                    cellQueuedOrNoData[fieldIndexNorth] = true;
                }
            }

            // south neighbor
            if (fieldIndexY < TreeCrownCostField.CapacityXY - 1)
            {
                int fieldIndexSouth = TreeCrownCostField.ToCellIndex(fieldIndexX, fieldIndexY + 1);
                if (cellQueuedOrNoData[fieldIndexSouth] == false)
                {
                    int southCellIndex = searchCellIndexY + 1;
                    if (crownNeighborhood.TryGetValue(searchCellIndexX, southCellIndex, out int southCrownID) && (southCrownID == treeID))
                    {
                        if (classificationNeighborhood.TryGetValue(searchCellIndexX, southCellIndex, out byte classification) == false)
                        {
                            classification = (byte)LandCoverClassification.Unclassified;
                        }

                        ++counts[classification];
                        searchQueue.Enqueue((searchCellIndexX, southCellIndex));
                    }

                    cellQueuedOrNoData[fieldIndexSouth] = true;
                }
            }
        }
    }
}
