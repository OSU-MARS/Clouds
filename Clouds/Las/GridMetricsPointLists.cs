using Mars.Clouds.GdalExtensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    public class GridMetricsPointLists : Grid<PointListZirnc>
    {
        public GridMetricsPointLists(RasterBand cellMask, LasTileGrid lasGrid)
            : base(cellMask.Crs, cellMask.Transform, cellMask.XSize, cellMask.YSize)
        {
            if ((this.Transform.ColumnRotation != 0.0) || (this.Transform.RowRotation != 0.0))
            {
                throw new NotSupportedException("Grid is rotated with respect to its coordinate system. This is not currently supported by the simple calculations used to intersect grid cells and point cloud tiles.");
            }
            if ((this.Transform.CellWidth > lasGrid.Transform.CellWidth) || (Double.Abs(this.Transform.CellHeight) > Double.Abs(lasGrid.Transform.CellHeight)))
            {
                throw new NotSupportedException("Grid cells are larger than point cloud tiles in at least one dimension. This is not currently supported by the simple calculations used to intersect grid cells and LiDAR tiles.");
            }

            // metrics grid might extend beyond tile grid, in which areas metrics cells need not be created as no points are available
            Debug.Assert(this.Transform.CellHeight < 0.0);
            (double lasGridXmin, double lasGridXmax, double lasGridYmin, double lasGridYmax) = lasGrid.GetExtent();
            (int metricsXindexMin, int metricsXindexMaxInclusive, int metricsYindexMin, int metricsYindexMaxInclusive) = this.GetIntersectingCellIndices(lasGridXmin, lasGridXmax, lasGridYmin, lasGridYmax);

            for (int metricsYindex = metricsYindexMin; metricsYindex <= metricsYindexMaxInclusive; ++metricsYindex)
            {
                for (int metricsXindex = metricsXindexMin; metricsXindex <= metricsXindexMaxInclusive; ++metricsXindex)
                {
                    // could possibly optimize for cellMask.HasNoData == false case but, for now, assume no data values are defined often
                    // enough (even if unused) it's not worth trying to avoid the virtual function call here as it'll be branch predicted
                    // Grid cells are typically large enough the number of checks needed is fairly low and typed access is difficult as
                    // C# lacks support for generic constructor arguments.
                    if (cellMask.IsNoData(metricsXindex, metricsYindex))
                    {
                        continue;
                    }

                    // four intersection possibilities given metrics cells and LiDAR/SfM tiles at the same rotation with cells being smaller
                    // than tiles
                    // Intersection testing is done by checking which tile each of the cell's corners is in. Assuming a conventional east-west,
                    // north-south aligned coordinate system,
                    //
                    // cell is entirely enclosed within one tile (mainline case): all four corners have same tile index
                    // cell crosses two tiles east-west: x min and max corner indices differ by 1, y min and max indices are the same
                    // cell crosses two tiles north-south: x min and max indices are the same, y min and max corner indices differ by 1
                    // cell overlaps four tiles: both x and y min and max indices differ
                    // 
                    // Given a fully populated tile grid there is no case where a metrics cell intersects with only three tiles. However,
                    // metrics cells may project past the range of LiDAR/SfM data availability.
                    (double metricsCellXmin, double metricsCellXmax, double metricsCellYmin, double metricsCellYmax) = this.Transform.GetCellExtent(metricsXindex, metricsYindex);
                    (int tileXindexMin, int tileXindexMaxInclusive, int tileYindexMin, int tileYindexMaxInclusive) = lasGrid.GetIntersectingCellIndices(metricsCellXmin, metricsCellXmax, metricsCellYmin, metricsCellYmax);

                    LasTile? tileXminYmin = lasGrid[tileXindexMin, tileYindexMin];
                    int tilesIntersected = tileXminYmin == null ? 0 : 1;
                    if (tileXindexMin != tileXindexMaxInclusive)
                    {
                        // check if cell spans two tiles east-west
                        LasTile? tileXmaxYmin = lasGrid[tileXindexMaxInclusive, tileYindexMin];
                        tilesIntersected += tileXmaxYmin == null ? 0 : 1;
                    }
                    if (tileYindexMin != tileYindexMaxInclusive)
                    {
                        // check if cell spans two tiles north-south
                        LasTile? tileXminYmax = lasGrid[tileXindexMin, tileYindexMaxInclusive];
                        tilesIntersected += tileXminYmax == null ? 0 : 1;

                        if (tileXindexMin != tileXindexMaxInclusive)
                        {
                            // check if cell spans two tiles both east-west and north-south
                            LasTile? tileXmaxYmax = lasGrid[tileXindexMaxInclusive, tileYindexMaxInclusive];
                            tilesIntersected += tileXmaxYmax == null ? 0 : 1;
                        }
                    }

                    int cellIndex = this.ToCellIndex(metricsXindex, metricsYindex);
                    this.Cells[cellIndex] = new(metricsXindex, metricsYindex, tilesIntersected);
                    ++this.NonNullCells;
                }
            }
        }

        public bool HasCellsInTile(LasTile tile)
        {
            (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) = this.GetIntersectingCellIndices(tile.GridExtent);
            for (int yIndex = yIndexMin; yIndex <= yIndexMaxInclusive; ++yIndex)
            {
                for (int xIndex = xIndexMin; xIndex <= xIndexMaxInclusive; ++xIndex)
                {
                    PointListZirnc? cell = this[xIndex, yIndex];
                    if (cell != null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void QueueCompletedCells(LasTile completedTile, BlockingCollection<List<PointListZirnc>> fullyPopulatedCellQueue)
        {
            Debug.Assert((this.Transform.ColumnRotation == 0.0) && (this.Transform.RowRotation == 0.0));

            List<PointListZirnc> completedCells = [];
            (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) = this.GetIntersectingCellIndices(completedTile.GridExtent);
            for (int yIndex = yIndexMin; yIndex <= yIndexMaxInclusive; ++yIndex)
            {
                for (int xIndex = xIndexMin; xIndex <= xIndexMaxInclusive; ++xIndex)
                {
                    PointListZirnc? cell = this[xIndex, yIndex];
                    if (cell == null)
                    {
                        continue;
                    }

                    // useful breakpoint in debugging loaded-intersected issues between tiles
                    //if (cell.TilesIntersected > 1)
                    //{
                    //    int q = 0;
                    //}
                    if (cell.TilesLoaded == cell.TilesIntersected)
                    {
                        completedCells.Add(cell);
                    }
                }
            }

            if (completedCells.Count > 0)
            {
                fullyPopulatedCellQueue.Add(completedCells);
            }
        }
    }
}
