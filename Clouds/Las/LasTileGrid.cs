using Mars.Clouds.Cmdlets.Drives;
using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    public class LasTileGrid : GridNullable<LasTile>
    {
        public int NonNullCells { get; protected set; }

        // accessible to unit tests
        internal LasTileGrid(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, IList<LasTile> tiles)
            : base(crs, transform, xSize, ySize)
        {
            Debug.Assert(this.Transform.CellHeight < 0.0);

            for (int tileIndex = 0; tileIndex < tiles.Count; ++tileIndex)
            {
                LasTile tile = tiles[tileIndex];
                (double xCentroid, double yCentroid) = tile.GridExtent.GetCentroid();
                (int xIndex, int yIndex) = this.ToGridIndices(xCentroid, yCentroid);
                // if needed, expand tile's grid extent to match the grid assuming tiles are anchored at their min (x, y) corner
                // This provides correction for tile sizes being slightly smaller than grid pitch. See LasTileGrid.Create().
                // An alternative to maintianing a separate grid extent per tile would be editing the LAS header's extents. However, this
                // is potentially confusing as the tile data does not change. An extra 32 bytes of memory per tile is not a concern.
                tile.GridExtent.XMax = Double.Max(tile.GridExtent.XMin + this.Transform.CellWidth, tile.GridExtent.XMax);
                tile.GridExtent.YMax = Double.Max(tile.GridExtent.YMin - this.Transform.CellHeight, tile.GridExtent.YMax);

                int cellIndex = this.ToCellIndex(xIndex, yIndex);
                Debug.Assert(this.Data[cellIndex] == null); // check for duplicate inserts
                this.Data[cellIndex] = tile;
            }

            this.NonNullCells = tiles.Count;
        }

        public static LasTileGrid Create(IList<LasTile> tiles, bool snap, int? requiredEpsg)
        {
            if (tiles.Count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(tiles));
            }

            LasTile tile = tiles[0];
            SpatialReference gridCrs = tile.GetSpatialReference();
            int tileEpsg = tile.GetProjectedCoordinateSystemEpsg();
            if (requiredEpsg.HasValue == false)
            {
                requiredEpsg = tileEpsg;
            }
            else if (tileEpsg != requiredEpsg)
            {
                throw new ArgumentOutOfRangeException(nameof(tiles), "Tile EPSG:" + tileEpsg + " does not match the tile grid's required EPSG of " + requiredEpsg + ".");
            }

            Extent tileExtent = tile.GridExtent;
            double gridMinX = tileExtent.XMin;
            double gridMaxMinX = tileExtent.XMin;
            double gridMaxX = tileExtent.XMax;
            double gridMinY = tileExtent.YMin;
            double gridMaxMinY = tileExtent.YMin;
            double gridMaxY = tileExtent.YMax;
            double tileReportedWidth = tileExtent.Width; // since small variations are allowed, could possibly be useful to track min and max width and height?
            double tileReportedHeight = tileExtent.Height;

            // best effort validation for consistency within a grid of tiles
            for (int tileIndex = 1; tileIndex < tiles.Count; ++tileIndex)
            {
                tile = tiles[tileIndex];
                tileEpsg = tile.GetProjectedCoordinateSystemEpsg();
                if (tileEpsg != requiredEpsg)
                {
                    throw new ArgumentOutOfRangeException(nameof(tiles), "Tile EPSG:" + tileEpsg + " does not match the tile grid's required EPSG of " + requiredEpsg + ".");
                }

                tileExtent = tile.GridExtent;
                double tileXextentInCrsUnits = tileExtent.Width;
                double tileYextentInCrsUnits = tileExtent.Height;
                if ((Double.Abs(tileXextentInCrsUnits / tileReportedWidth - 1.0) > 0.000001) ||
                    (Double.Abs(tileYextentInCrsUnits / tileReportedHeight - 1.0) > 0.000001))
                {
                    throw new ArgumentOutOfRangeException(nameof(tiles), "Tile " + tileIndex + " has extents (" + tileExtent.XMin + ", " + tileExtent.XMax + ", " + tileExtent.YMin + ", " + tileExtent.YMax + ") in EPSG:" + tileEpsg + " with a width of " + tileXextentInCrsUnits + " and height of " + tileYextentInCrsUnits + ".  This does not match the expected width " + tileReportedWidth + " and height " + tileReportedHeight + ".");
                }

                // LAS files have only a bounding box, so no way to check whether tile is in fact a rectangle (or square) aligned to its
                // coordinate system, if it's rotated with respect to the coordinate system, or if it's some more complex shape without
                // loading and inspecting all of its points.

                if (tileExtent.XMin < gridMinX)
                {
                    gridMinX = tileExtent.XMin;
                }
                if (tileExtent.XMin > gridMaxMinX)
                {
                    gridMaxMinX = tileExtent.XMin;
                }
                if (tileExtent.XMax > gridMaxX)
                {
                    gridMaxX = tileExtent.XMax;
                }
                if (tileExtent.YMin < gridMinY)
                {
                    gridMinY = tileExtent.YMin;
                }
                if (tileExtent.YMin > gridMaxMinY)
                {
                    gridMaxMinY = tileExtent.YMin;
                }
                if (tileExtent.YMax > gridMaxY)
                {
                    gridMaxY = tileExtent.YMax;
                }
            }

            double gridTotalWidth = gridMaxX - gridMinX;
            double gridTotalHeight = gridMaxY - gridMinY;
            int gridXsizeInCells = (int)(gridTotalWidth / tileReportedWidth + 0.5);
            int gridYSizeInCells = (int)(gridTotalHeight / tileReportedHeight + 0.5);
            // ideally point cloud tiles' reported extent exactly matches the grid pitch
            // However, this is not always the case as some LiDAR vendors generate LAS headers with small gaps (a few millimeters) between
            // tiles. If such a gap aligns with a break in the ABA grid then tile hit testing of ABA cell corners against tile extents will
            // fail (modulo numerical precision) as an ABA corner over a gap is not part of any tile. Alternatively, tiles may have some
            // overlap, in which case the tile size is larger than the grid's effective cell size rather than smaller.
            // For now, assume tiles are anchored from their minimum (x, y) corner and calculate grid cell size as the mean spacing across
            // the grid from the minimum (x, y) corner to the grid cell whose minimum corner has the largest values available. Assuming tiles
            // are identically shrunk or expanded, this method is robust to both cases in recovering the grid spacing.
            double gridCellWidth = gridXsizeInCells > 1 ? (gridMaxMinX - gridMinX) / (gridXsizeInCells - 1) : gridTotalWidth;
            double gridCellHeight = gridYSizeInCells > 1 ? (gridMaxMinY - gridMinY) / (gridYSizeInCells - 1) : gridTotalHeight;
            double gridOriginY = gridYSizeInCells > 1 ? Double.Max(gridMaxMinY + gridCellHeight, gridMaxY) : gridMaxY;

            if (snap)
            {
                // floor and ceiling to get min X and origin Y might move tile more than rounding up the width and height extends the grid
                // Currently unclear how best to deal with such cases, though adding an extra 1.0 CRS linear units to the width and height
                // seems a reasonable default if handling proves to be needed.
                gridCellWidth = Double.Ceiling(gridCellWidth);
                gridCellHeight = gridCellHeight > 0.0 ? Double.Ceiling(gridCellHeight) : Double.Floor(gridCellHeight);
                gridMinX = Double.Floor(gridMinX);
                gridOriginY = Double.Ceiling(gridOriginY);
            }

            GridGeoTransform gridTransform = new(gridMinX, gridOriginY, gridCellWidth, -gridCellHeight);
            return new LasTileGrid(gridCrs, gridTransform, gridXsizeInCells, gridYSizeInCells, tiles);
        }
    }
}
