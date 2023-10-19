using Mars.Clouds.GdalExtensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    public class AbaGrid : Grid<PointListZirnc>
    {
        public AbaGrid(Raster<UInt16> abaCellDefinitions, LasTileGrid lasGrid)
            : base(abaCellDefinitions.Crs, abaCellDefinitions.Transform, abaCellDefinitions.XSize, abaCellDefinitions.YSize)
        {
            if ((this.Transform.ColumnRotation != 0.0) || (this.Transform.RowRotation != 0.0))
            {
                throw new NotSupportedException("Grid is rotated with respect to its coordinate system. This is not currently supported by the simple calculations used to intersect ABA grid cells and point cloud tiles.");
            }
            if ((this.Transform.CellWidth > lasGrid.Transform.CellWidth) || (Double.Abs(this.Transform.CellHeight) > Double.Abs(lasGrid.Transform.CellHeight)))
            {
                throw new NotSupportedException("ABA grid cells are larger than point cloud tiles in at least one dimension. This is not currently supported by the simple calculations used to intersect ABA grid cells and LiDAR tiles.");
            }
            if (abaCellDefinitions.Bands.Length != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(abaCellDefinitions));
            }

            // ABA grid might extend beyond tile grid, in which areas ABA cells need not be created as no points are available
            Debug.Assert(this.Transform.CellHeight < 0.0);
            (double lasGridXmin, double lasGridXmax, double lasGridYmin, double lasGridYmax) = lasGrid.GetExtent();
            (int abaXindexMin, int abaXindexMaxInclusive, int abaYindexMin, int abaYindexMaxInclusive) = this.GetIntersectingCellIndices(lasGridXmin, lasGridXmax, lasGridYmin, lasGridYmax);

            RasterBand < UInt16> abaCellMask = abaCellDefinitions.Bands[0];
            for (int abaYindex = abaYindexMin; abaYindex <= abaYindexMaxInclusive; ++abaYindex)
            {
                for (int abaXindex = abaXindexMin; abaXindex <= abaXindexMaxInclusive; ++abaXindex)
                {
                    UInt16 maskValue = abaCellMask[abaXindex, abaYindex];
                    if (abaCellMask.IsNoData(maskValue))
                    {
                        continue;
                    }

                    // four intersection possibilities given ABA cells and LiDAR/SfM tiles at the same rotation with cells being smaller than tiles
                    // Intersection testing is done by checking which tile each of the cell's corners is in. Assuming a conventional east-west,
                    // north-south aligned coordinate system,
                    //
                    // cell is entirely enclosed within one tile (mainline case): all four corners have same tile index
                    // cell crosses two tiles east-west: x min and max corner indices differ by 1, y min and max indices are the same
                    // cell crosses two tiles north-south: x min and max indices are the same, y min and max corner indices differ by 1
                    // cell overlaps four tiles: both x and y min and max indices differ
                    // 
                    // Given a fully populated tile grid there is no case where an ABA cell intersects with only three tiles. However, ABA cells
                    // may project past the range of LiDAR/SfM data availability.
                    (double abaCellXmin, double abaCellXmax, double abaCellYmin, double abaCellYmax) = this.Transform.GetCellExtent(abaXindex, abaYindex);
                    (int tileXindexMin, int tileXindexMaxInclusive, int tileYindexMin, int tileYindexMaxInclusive) = lasGrid.GetIntersectingCellIndices(abaCellXmin, abaCellXmax, abaCellYmin, abaCellYmax);

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

                    int cellIndex = this.ToCellIndex(abaXindex, abaYindex);
                    this.Cells[cellIndex] = new(abaXindex, abaYindex, tilesIntersected);
                    ++this.NonNullCells;
                }
            }
        }

        public bool HasCellsInTile(LasFile tile)
        {
            (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) = this.GetIntersectingCellIndices(tile.Header.MinX, tile.Header.MaxX, tile.Header.MinY, tile.Header.MaxY);
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

        public void QueueCompletedCells(LasFile completedTile, BlockingCollection<List<PointListZirnc>> abaFullyPopulatedCellQueue)
        {
            Debug.Assert((this.Transform.ColumnRotation == 0.0) && (this.Transform.RowRotation == 0.0));

            List<PointListZirnc> completedCells = new();
            (int xIndexMin, int xIndexMaxInclusive, int yIndexMin, int yIndexMaxInclusive) = this.GetIntersectingCellIndices(completedTile.Header.MinX, completedTile.Header.MaxX, completedTile.Header.MinY, completedTile.Header.MaxY);
            for (int yIndex = yIndexMin; yIndex <= yIndexMaxInclusive; ++yIndex)
            {
                for (int xIndex = xIndexMin; xIndex <= xIndexMaxInclusive; ++xIndex)
                {
                    PointListZirnc? cell = this[xIndex, yIndex];
                    if (cell == null)
                    {
                        continue;
                    }

                    if (cell.TilesLoaded == cell.TilesIntersected)
                    {
                        completedCells.Add(cell);
                    }
                }
            }

            if (completedCells.Count > 0)
            {
                abaFullyPopulatedCellQueue.Add(completedCells);
            }
        }
    }
}
