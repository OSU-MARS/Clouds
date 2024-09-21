using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;

namespace Mars.Clouds.Segmentation
{
    public class TreetopsGrid : Grid<Treetops>
    {
        private const int DefaultCapacityIncrement = 1000; // 1k trees per cell

        public TreetopsGrid(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells, long totalTreesInGrid, bool cloneCrsAndTransform)
            : base(crs, transform, xSizeInCells, ySizeInCells, cloneCrsAndTransform)
        {
            int initialTreetopCapacityPerCell = TreetopsGrid.DefaultCapacityIncrement * ((int)(totalTreesInGrid / (TreetopsGrid.DefaultCapacityIncrement * xSizeInCells * ySizeInCells)) + 1);
            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this[cellIndex] = new(initialTreetopCapacityPerCell, xyIndices: true, classCapacity: 0);
            }
        }

        public void Reset(Grid newExtents, int subsamplingRatio)
        {
            // inherited from Grid
            int subsampledSizeX = newExtents.SizeX / subsamplingRatio;
            int subsampledSizeY = newExtents.SizeY / subsamplingRatio;
            if ((this.SizeX != subsampledSizeX) || (this.SizeY != subsampledSizeY))
            {
                throw new NotSupportedException(nameof(this.Reset) + "() does not currently support changing the grid size from " + this.SizeX + " x " + this.SizeY + " cells to " + subsampledSizeX + " x " + subsampledSizeY + ".");
            }

            this.Transform.Copy(newExtents.Transform, subsamplingRatio);

            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this[cellIndex].Clear();
            }
        }
    }
}
