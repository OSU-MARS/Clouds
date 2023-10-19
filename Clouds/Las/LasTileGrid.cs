using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    public class LasTileGrid : GridDisposable<LasTile>
    {
        public LasTileGrid(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, IList<LasTile> tiles)
            : base(crs, transform, xSize, ySize)
        {
            for (int tileIndex = 0; tileIndex < tiles.Count; ++tileIndex)
            {
                LasTile tile = tiles[tileIndex];
                (double xCentroid, double yCentroid) = tile.File.Header.GetCentroidXY();
                (int xIndex, int yIndex) = this.Transform.GetCellIndex(xCentroid, yCentroid);
                int cellIndex = this.ToCellIndex(xIndex, yIndex);
                Debug.Assert(this.Cells[cellIndex] == null); // check for duplicate inserts
                this.Cells[cellIndex] = tile;
            }

            this.NonNullCells = tiles.Count;
        }

        public static LasTileGrid Create(IList<LasTile> tiles, int requiredEpsg)
        {
            if (tiles.Count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(tiles));
            }

            LasTile tile = tiles[0];
            LasHeader10 lasHeader = tile.File.Header;
            SpatialReference gridCrs = tile.File.GetSpatialReference();
            int tileEpsg = tile.File.GetProjectedCoordinateSystemEpsg();
            if (tileEpsg != requiredEpsg)
            {
                throw new ArgumentOutOfRangeException(nameof(tiles), "Tile EPSG:" + tileEpsg + " does not match the tile grid's required EPSG of " + requiredEpsg + ".");
            }

            double gridMinX = lasHeader.MinX;
            double gridMaxX = lasHeader.MaxX;
            double gridMinY = lasHeader.MinY;
            double gridMaxY = lasHeader.MaxY;
            double gridCellXextentInCrsUnits = lasHeader.MaxX - lasHeader.MinX;
            double gridCellYextentInCrsUnits = lasHeader.MaxY - lasHeader.MinY;

            // best effort validation for consistency within a grid of tiles
            for (int tileIndex = 1; tileIndex < tiles.Count; ++tileIndex)
            {
                tile = tiles[tileIndex];
                tileEpsg = tile.File.GetProjectedCoordinateSystemEpsg();
                if (tileEpsg != requiredEpsg)
                {
                    throw new ArgumentOutOfRangeException(nameof(tiles), "Tile EPSG:" + tileEpsg + " does not match the tile grid's required EPSG of " + requiredEpsg + ".");
                }

                lasHeader = tile.File.Header;
                double tileXextentInCrsUnits = lasHeader.MaxX - lasHeader.MinX;
                double tileYextentInCrsUnits = lasHeader.MaxY - lasHeader.MinY;
                if ((Double.Abs(tileXextentInCrsUnits / gridCellXextentInCrsUnits - 1.0) > 0.000001) ||
                    (Double.Abs(tileYextentInCrsUnits / gridCellYextentInCrsUnits - 1.0) > 0.000001))
                {
                    throw new ArgumentOutOfRangeException(nameof(tiles), "Tile " + tileIndex + " has extents (" + lasHeader.MinX + ", " + lasHeader.MaxX + ", " + lasHeader.MinY + ", " + lasHeader.MaxY + ") in EPSG:" + tileEpsg + " with a width of " + tileXextentInCrsUnits + " and height of " + tileYextentInCrsUnits + ".  This does not match the expected width " + gridCellXextentInCrsUnits + " and height " + gridCellYextentInCrsUnits + ".");
                }

                // LAS files have only a bounding box, so no way to check whether tile is in fact a rectangle (or square) aligned to its
                // coordinate system, if it's rotated with respect to the coordinate system, or if it's some more complex shape without
                // loading and inspecting all of its points.

                if (lasHeader.MinX < gridMinX)
                {
                    gridMinX = lasHeader.MinX;
                }
                if (lasHeader.MaxX > gridMaxX)
                {
                    gridMaxX = lasHeader.MaxX;
                }
                if (lasHeader.MinY < gridMinY)
                {
                    gridMinY = lasHeader.MinY;
                }
                if (lasHeader.MaxY > gridMaxY)
                {
                    gridMaxY = lasHeader.MaxY;
                }
            }

            GridGeoTransform gridTransform = new(gridMinX, gridMaxY, gridCellXextentInCrsUnits, -gridCellYextentInCrsUnits);
            int gridSizeX = (int)((gridMaxX - gridMinX) / gridCellXextentInCrsUnits + 0.5);
            int gridSizeY = (int)((gridMaxY - gridMinY) / gridCellYextentInCrsUnits + 0.5);
            return new LasTileGrid(gridCrs, gridTransform, gridSizeX, gridSizeY, tiles);
        }
    }
}
