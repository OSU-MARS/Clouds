using Mars.Clouds.GdalExtensions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Las
{
    public class PointListGridZs
    {
        public Grid<List<float>> Z { get; private init; }
        public Grid<List<UInt16>> SourceID { get; private init; }

        public PointListGridZs(Grid extent, int initialCellPointCapacity)
        {
            this.SourceID = new(extent, cloneCrsAndTransform: false);
            this.Z = new(extent, cloneCrsAndTransform: false);

            for (int cellIndex = 0; cellIndex < this.SourceID.Cells; ++cellIndex)
            {
                this.SourceID[cellIndex] = new(initialCellPointCapacity);
            }
            for (int cellIndex = 0; cellIndex < this.Z.Cells; ++cellIndex)
            {
                this.Z[cellIndex] = new(initialCellPointCapacity);
            }
        }

        public static void CreateRecreateOrReset(Grid dtmTile, int newCellPointCapacity, [NotNull] ref PointListGridZs? listGrid)
        {
            if ((listGrid == null) || (listGrid.Z.SizeX != dtmTile.SizeX) || (listGrid.Z.SizeY != dtmTile.SizeY))
            {
                listGrid = new(dtmTile, newCellPointCapacity);
                return;
            }

            listGrid.Z.Transform.Copy(dtmTile.Transform);
            for (int cellIndex = 0; cellIndex < listGrid.Z.Cells; ++cellIndex)
            {
                listGrid.Z[cellIndex].Clear();
            }

            listGrid.SourceID.Transform.Copy(dtmTile.Transform);
            for (int cellIndex = 0; cellIndex < listGrid.SourceID.Cells; ++cellIndex)
            {
                listGrid.SourceID[cellIndex].Clear();
            }
        }
    }
}
