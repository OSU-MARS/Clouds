using Mars.Clouds.GdalExtensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Las
{
    // TODO: investigate GC offload, possibly by slicing lists from large object heap size arrays
    // Also https://github.com/dotnet/runtime/discussions/73699
    //      https://github.com/dotnet/runtime/discussions/47198
    //      https://github.com/dotnet/runtime/issues/27023, https://github.com/jtmueller/Collections.Pooled.
    public class PointListGridZs
    {
        // tupled list is 10-20% more efficient for Get-Dsm than using two lists, depending on number of memory allocations
        public Grid<List<(float z, UInt16 sourceID)>> ZSourceID { get; private init; }

        public PointListGridZs(Grid extent, int initialCellPointCapacity)
        {
            this.ZSourceID = new(extent, cloneCrsAndTransform: false);
            for (int cellIndex = 0; cellIndex < this.ZSourceID.Cells; ++cellIndex)
            {
                this.ZSourceID[cellIndex] = new(initialCellPointCapacity);
            }
        }

        /// <param name="newCellPointCapacity">Initial capacity of cells' point lists if grid is created or recreated. Ignored if an existing grid is reset</param>
        public static void CreateRecreateOrReset(Grid dtmTile, float expectedPointsPerTile, [NotNull] ref PointListGridZs? listGrid)
        {
            if ((listGrid == null) || (listGrid.ZSourceID.SizeX != dtmTile.SizeX) || (listGrid.ZSourceID.SizeY != dtmTile.SizeY))
            {
                // favor initial size above mean to reduce likelihood of reallocation
                // Profiling shows meaningful advantage to using semi-aligned List<> sizes, so margin the mean and round mean up to the nearest
                // multiple of eight points. Allocating above the mean trades memory for performance, partly on the assumption the first tile
                // presented is not the densest one to be processed.
                // Caller could use statistics from the .las tile grid to make better estimates.
                float meanPointsPerCell = expectedPointsPerTile / (float)dtmTile.Cells;
                int initialCellPointCapacity = 8 * (int)Single.Ceiling(1.4F * meanPointsPerCell / 8.0F);
                listGrid = new(dtmTile, initialCellPointCapacity);
                return;
            }

            Debug.Assert(SpatialReferenceExtensions.IsSameCrs(listGrid.ZSourceID.Crs, dtmTile.Crs));
            listGrid.ZSourceID.Transform.Copy(dtmTile.Transform);
            for (int cellIndex = 0; cellIndex < listGrid.ZSourceID.Cells; ++cellIndex)
            {
                listGrid.ZSourceID[cellIndex].Clear();
            }
        }
    }
}
