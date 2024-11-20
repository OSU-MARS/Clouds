using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    public class GridMetricsPointLists : Grid<PointListZirnc>
    {
        public GridMetricsPointLists(LasTileGrid lasGrid, LasTile lasTile, double cellSize, int xSizeInCells, int ySizeInCells, RasterBand? metricsCellMask)
            : base(lasGrid.Crs.Clone(), new(lasTile.GridExtent.XMin, lasTile.GridExtent.YMax, cellSize, -cellSize), xSizeInCells, ySizeInCells, cloneCrsAndTransform: true)
        {
            Debug.Assert((this.Transform.ColumnRotation == 0.0) && (this.Transform.RowRotation == 0.0), "Grid is rotated with respect to its coordinate system. This is not currently supported by the simple calculations used to intersect grid cells and point cloud tiles.");

            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this[cellIndex] = new();
            }

            this.Reset(lasGrid, lasTile, metricsCellMask);
        }

        public int EvaluateCompleteAndAccumulateIncompleteCells(GridMetricsRaster metricsRaster, RasterNeighborhood8<float> dtmNeighborhood, float heightClassSizeInCrsUnits, float zThresholdInCrsUnits, ObjectPool<PointListZirnc> pointListPool)
        {
            (double xMin, double xMax, double yMin, double yMax) = this.GetExtent();
            (int metricsIndexXmin, int _, int metricsIndexYmin, int _) = metricsRaster.GetIntersectingCellIndices(xMin, xMax, yMin, yMax);
            int cellsCompleted = 0;
            for (int yIndex = 0; yIndex < this.SizeY; ++yIndex)
            {
                int metricsIndexY = metricsIndexYmin + yIndex;
                for (int xIndex = 0; xIndex < this.SizeX; ++xIndex)
                {
                    PointListZirnc? cellPoints = this[xIndex, yIndex];
                    if ((cellPoints == null) || (cellPoints.TilesLoaded == 0))
                    {
                        continue;
                    }

                    // useful breakpoint for debugging loaded-intersected issues
                    //if (cell.TilesIntersected > 1)
                    //{
                    //    int q = 0;
                    //}
                    int metricsIndexX = metricsIndexXmin + xIndex;
                    if (cellPoints.TilesLoaded == cellPoints.TilesIntersected) // generally applicable but likely only reached for TilesLoaded = 1 and TilesIntersected = 1
                    {
                        Debug.Assert(cellPoints.TilesLoaded > 0);
                        metricsRaster.SetMetrics(metricsIndexX, metricsIndexY, cellPoints, dtmNeighborhood, heightClassSizeInCrsUnits, zThresholdInCrsUnits);
                        ++cellsCompleted;
                    }
                    else
                    {
                        if (metricsRaster.AccumulatePointsThreadSafe(metricsIndexX, metricsIndexY, cellPoints, pointListPool, out PointListZirnc? completeCellPoints))
                        {
                            metricsRaster.SetMetrics(metricsIndexX, metricsIndexY, completeCellPoints, dtmNeighborhood, heightClassSizeInCrsUnits, zThresholdInCrsUnits);
                            ++cellsCompleted;
                        }
                    }
                }
            }
            
            return cellsCompleted;
        }

        /// <summary>
        /// Move point list grid to tile, set cell counts back to zero, and set tile intersection counts.
        /// </summary>
        /// <param name="lasGrid">Grid of point cloud tiles. Not used unless a cell mask is specified.</param>
        public void Reset(LasTileGrid lasGrid, LasTile lasTile, RasterBand? metricsCellMask)
        {
            Debug.Assert(this.Transform.CellHeight < 0.0, "Cell height is " + this.Transform.CellHeight + ". Non-negative heights are not currently supported.");

            // reset all metrics cells
            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this[cellIndex].Reset();
            }

            // maskless case: shift origin to match point cloud tile and return
            // Cells are tile aligned, each cell therefore intersects one tile, and no further action's needed.
            if (metricsCellMask == null)
            {
                this.Transform.SetOrigin(lasTile.GridExtent.XMin, lasTile.GridExtent.YMax);
                return;
            }

            // align origin to cell mask
            Debug.Assert((this.Transform.CellWidth == metricsCellMask.Transform.CellWidth) && (this.Transform.CellHeight == metricsCellMask.Transform.CellHeight));
            (double metricsXmin, double metricsXmax, double metricsYmin, double metricsYmax) = this.GetExtent();
            (int maskOriginIndexX, int maskIndexXmaxInclusive, int maskOriginIndexY, int maskIndexYmaxInclusive) = metricsCellMask.GetIntersectingCellIndices(metricsXmin, metricsXmax, metricsYmin, metricsYmax);
            double originX = metricsCellMask.Transform.OriginX + metricsCellMask.Transform.CellWidth * maskOriginIndexX;
            double originY = metricsCellMask.Transform.OriginY + metricsCellMask.Transform.CellHeight * maskOriginIndexY;
            this.Transform.SetOrigin(originX, originY);

            // set cell intersection counts based on mask and positioning across point cloud tiles
            // Four intersection cases are possible, the first three of which should be unreachable in the current design due to point
            // list tiles being one cell larger in x and y than point cloud tiles to handle the most common case of crossing point cloud 
            // tile boundaries in both x and y.
            // - Point list tile might align with point cloud tile, meaning all metrics cells default to a tile intersection count of one.
            //   If a cell is masked off it is marked with an intersection count of zero.
            // - Point list tile crosses point cloud tile boundary in the x direction, in which case there will be a line of metrics cells
            //   in the y (north-south) direction which intersect two tiles. Unless edges of the tiles and metrics cells happen to exactly
            //   align or one of the positions in the point cloud tile grid is not occupied, in which cases the intersection counts remain
            //   one.
            // - Point list tile crosses point cloud tile boundary in the y direction, which yields an x directed (east-west) line of 
            //   metrics cells intersecting two tiles, subject to the same caveats as an x boundary crossing.
            // - Point list tile crosses both x and point cloud tile boundaries and therefore has y and x lines of cells. Where the lines
            //   intersect the tile intersection count is four, again subject to caveats.
            // Since the point list tile floats with the point cloud tile, subject to snapping to the cell mask, intersection lines occur
            // only at the point list tiles' edges. Most commonly, lines will occur at all four edges, resulting in a 3x3 neighborhood
            // where intersection counts are determined by neighbors' presence. When cell mask boundaries align with point cloud tiles' 
            // boundaries a 3x2, 2x3, or 2x2 neighborhood can result. Locations at the edge of the point cloud tiles can also produce
            // neighborhoods smaller than 3x3, including also 3x1, 1x3, 2x1, 1x2, and, in the case of a single cloud, 1x1.
            Debug.Assert(lasGrid.Transform.CellHeight < 0.0, "Point cloud tile height is " + lasGrid.Transform.CellHeight + ". Non-negative heights are not currently supported.");

            (double metricsCentroidX, double metricsCentroidY) = this.GetCentroid();
            (int lasGridCenterIndexX, int lasGridCenterIndexY) = lasGrid.ToGridIndices(metricsCentroidX, metricsCentroidY);
            // (int lasGridWesternmostIndexX, int lasGridEasternmostIndexX, int lasGridSouthernmostIndexY, int lasGridNorthernmostIndexY) = lasGrid.GetIntersectingCellIndices(metricsXmin, metricsXmax, metricsYmin, metricsYmax);
            bool canHaveNeighborNorth = metricsYmax > lasTile.GridExtent.YMax;
            bool canHaveNeighborSouth = metricsYmin < lasTile.GridExtent.YMin;
            bool canHaveNeighborEast = metricsXmax > lasTile.GridExtent.XMax;
            bool canHaveNeighborWest = metricsXmin > lasTile.GridExtent.XMin;

            int lasGridNorthIndexY = lasGridCenterIndexY - 1;
            int lasGridSouthIndexY = lasGridCenterIndexY + 1;
            int lasGridEastIndexX = lasGridCenterIndexX + 1;
            int lasGridWestIndexX = lasGridCenterIndexX - 1;

            bool hasNeighborNorth = canHaveNeighborNorth && lasGrid.TryGetValue(lasGridCenterIndexX, lasGridNorthIndexY, out LasTile? _);
            bool hasNeighborSouth = canHaveNeighborSouth && lasGrid.TryGetValue(lasGridCenterIndexX, lasGridSouthIndexY, out LasTile? _);
            bool hasNeighborEast = canHaveNeighborEast && lasGrid.TryGetValue(lasGridEastIndexX, lasGridCenterIndexY, out LasTile? _);
            bool hasNeighborWest = canHaveNeighborWest && lasGrid.TryGetValue(lasGridWestIndexX, lasGridCenterIndexY, out LasTile? _);

            bool hasNeighborNorthwest = canHaveNeighborNorth && canHaveNeighborWest && lasGrid.TryGetValue(lasGridWestIndexX, lasGridNorthIndexY, out LasTile? _);
            bool hasNeighborNortheast = canHaveNeighborNorth && canHaveNeighborEast && lasGrid.TryGetValue(lasGridEastIndexX, lasGridNorthIndexY, out LasTile? _);
            bool hasNeighborSouthwest = canHaveNeighborSouth && canHaveNeighborWest && lasGrid.TryGetValue(lasGridWestIndexX, lasGridSouthIndexY, out LasTile? _);
            bool hasNeighborSoutheast = canHaveNeighborSouth && canHaveNeighborEast && lasGrid.TryGetValue(lasGridEastIndexX, lasGridSouthIndexY, out LasTile? _);

            int maxInteriorIndexXexclusive = this.SizeX - 1;
            int maxInteriorIndexYexclusive = this.SizeY - 1;
            for (int metricsIndexY = 0; metricsIndexY < this.SizeY; ++metricsIndexY)
            {
                int maskIndexY = maskOriginIndexY + metricsIndexY;
                bool offMaskY = (maskIndexY < 0) || (maskIndexY > maskIndexYmaxInclusive);
                int maskIndexX = maskOriginIndexX;

                // first (westernmost) cell in row
                bool offMaskX = (maskIndexX < 0) || (maskIndexX > maskIndexXmaxInclusive);
                if (offMaskX || offMaskY || metricsCellMask.IsNoData(maskIndexX, maskIndexY))
                {
                    this[0, metricsIndexY].TilesIntersected = 0;
                }
                else if (metricsIndexY == 0) // northwestern corner
                {
                    int tilesIntersected = 1 + (hasNeighborNorth ? 1 : 0) + (hasNeighborNorthwest ? 1 : 0) + (hasNeighborWest ? 1 : 0);
                    this[0, metricsIndexY].TilesIntersected = tilesIntersected;
                }
                else if (metricsIndexY == maxInteriorIndexYexclusive) // southwestern corner
                {
                    int tilesIntersected = 1 + (hasNeighborSouth ? 1 : 0) + (hasNeighborSouthwest ? 1 : 0) + (hasNeighborWest ? 1 : 0);
                    this[0, metricsIndexY].TilesIntersected = tilesIntersected;
                }
                else if (hasNeighborWest) // western edge
                {
                    this[0, metricsIndexY].TilesIntersected = 2;
                }

                // interior cells in row
                for (int metricsIndexX = 0; metricsIndexX < maxInteriorIndexXexclusive; ++maskIndexX, ++metricsIndexX)
                {
                    offMaskX = (maskIndexX < 0) || (maskIndexX > maskIndexXmaxInclusive);
                    if (offMaskX || offMaskY || metricsCellMask.IsNoData(maskIndexX, maskIndexY))
                    {
                        this[metricsIndexX, metricsIndexY].TilesIntersected = 0;
                    }
                    else if (metricsIndexY == 0) // northern edge
                    {
                        int tilesIntersected = 1 + (hasNeighborNorth ? 1 : 0);
                        this[metricsIndexX, metricsIndexY].TilesIntersected = tilesIntersected;
                    }
                    else if (metricsIndexY == maxInteriorIndexYexclusive) // southern edge
                    {
                        int tilesIntersected = 1 + (hasNeighborSouth ? 1 : 0);
                        this[metricsIndexX, metricsIndexY].TilesIntersected = tilesIntersected;
                    }
                    // cell is interior and already reset to 1
                }

                // last (easternmost) cell in row
                offMaskX = (maskIndexX < 0) || (maskIndexX > maskIndexXmaxInclusive);
                if (offMaskX || offMaskY || metricsCellMask.IsNoData(maskIndexX, maskIndexY))
                {
                    this[maxInteriorIndexXexclusive, metricsIndexY].TilesIntersected = 0;
                }
                else if (metricsIndexY == 0) // northeastern corner
                {
                    int tilesIntersected = 1 + (hasNeighborNorth ? 1 : 0) + (hasNeighborNortheast ? 1 : 0) + (hasNeighborEast ? 1 : 0);
                    this[0, metricsIndexY].TilesIntersected = tilesIntersected;
                }
                else if (metricsIndexY == maxInteriorIndexYexclusive) // southeastern corner
                {
                    int tilesIntersected = 1 + (hasNeighborNorth ? 1 : 0) + (hasNeighborSoutheast ? 1 : 0) + (hasNeighborEast ? 1 : 0);
                    this[0, metricsIndexY].TilesIntersected = tilesIntersected;
                }
                else if (hasNeighborEast) // eastern edge
                {
                    this[maxInteriorIndexXexclusive, metricsIndexY].TilesIntersected = 2;
                }
            }
        }
    }
}
