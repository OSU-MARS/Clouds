using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Cmdlets
{
    /// <summary>
    /// Strack streaming of virtual raster tiles through memory for read or creation.
    /// </summary>
    public class TileStreamPosition<TTile> where TTile : class
    {
        /// <summary>
        /// Greatest x and y index of tile with continguous completion, packed for atomic updates and thread safe interaction between 
        /// <see cref="IsCompleteTo(int, int)" and <see cref="OnTileCompleted(int, int)"/>. Used for checking neighborhood completion. 
        /// X index is in the in upper 32 bits, y in the lower 32.
        /// </summary>
        /// <remarks>
        /// All tiles from [0, 0] to [completedX, completedY] have been marked complete. Due to out of order completion by threads it is likely
        /// some tiles at higher indices have also been completed.
        /// </remarks>
        private long completedIndexXY;

        private readonly bool[,] isTileCompleted;
        private int pendingIndexX;
        private int pendingIndexY;

        private readonly bool rasterTileGrid; // worth making this static?
        protected int PendingRowFreeIndex { get; set; }

        /// <summary>
        /// Greatest y index of a row with contiguous completion for all of its tiles and all previous tiles. Used for releasing tiles
        /// once a row is no longer within reach of an active neighborhood.
        /// </summary>
        public int CompletedRowIndex { get; private set; }

        public GridNullable<TTile> TileGrid { get; private init; }

        public TileStreamPosition(GridNullable<TTile> tileGrid, bool[,] unpopulatedTileMap)
        {
            this.completedIndexXY = Int64Extensions.Pack(-1, -1);
            this.CompletedRowIndex = -1;
            this.isTileCompleted = unpopulatedTileMap;
            this.PendingRowFreeIndex = 0;
            this.pendingIndexX = 0;
            this.pendingIndexY = 0;

            this.rasterTileGrid = typeof(TTile).IsSubclassOf(typeof(Raster));
            this.TileGrid = tileGrid;
        }

        public bool IsCompleteTo(int tileIndexX, int tileIndexY)
        {
            Debug.Assert((tileIndexX >= 0) && (tileIndexX < this.TileGrid.SizeX) && (tileIndexY >= 0) && (tileIndexY < this.TileGrid.SizeY));

            int completedIndexY = this.completedIndexXY.GetLowerInt32();
            if (completedIndexY < tileIndexY)
            {
                return false;
            }
            if (completedIndexY == tileIndexY) 
            {
                int completedIndexX = this.completedIndexXY.GetUpperInt32();
                return tileIndexX <= completedIndexX;
            }

            Debug.Assert(this.isTileCompleted[tileIndexX, tileIndexY]); // sanity check
            return true;
        }

        public void OnTileCompleted(int tileIndexX, int tileIndexY)
        {
            this.isTileCompleted[tileIndexX, tileIndexY] = true;

            // scan to see if one or more rows of tiles has been completed since the last call
            int vrtSizeInTilesX = this.TileGrid.SizeX;
            int vrtSizeInTilesY = this.TileGrid.SizeY;
            int maxXindexWithContinguousCompletion = this.completedIndexXY.GetUpperInt32();
            int maxYindexWithContinguousCompletion = this.completedIndexXY.GetLowerInt32();
            int startIndexX = maxXindexWithContinguousCompletion == vrtSizeInTilesX - 1 ? 0 : maxXindexWithContinguousCompletion + 1;
            int startIndexY = this.CompletedRowIndex + 1;
            for (int yIndex = startIndexY; yIndex < vrtSizeInTilesY; ++yIndex)
            {
                bool rowIncomplete = false;
                for (int xIndex = startIndexX; xIndex < vrtSizeInTilesX; ++xIndex)
                {
                    if (this.isTileCompleted[xIndex, yIndex])
                    {
                        maxXindexWithContinguousCompletion = xIndex;
                        maxYindexWithContinguousCompletion = yIndex;
                    }
                    else
                    {
                        rowIncomplete = true;
                        break;
                    }
                }

                if (rowIncomplete)
                {
                    break;
                }
                else
                {
                    startIndexX = 0;
                    this.CompletedRowIndex = yIndex;
                }
            }

            this.completedIndexXY = Int64Extensions.Pack(maxXindexWithContinguousCompletion, maxYindexWithContinguousCompletion);
        }

        public bool TryGetNextTile(out int tileIndexX, out int tileIndexY, [NotNullWhen(true)] out TTile? nextTile)
        {
            // see if a tile is available for write a row basis
            // In the single row case, tile writes can begin as soon as their +x neighbor's been created. This is not currently
            // handled. Neither are more complex cases where voids in the grid permit writes to start before the next row of DSM
            // tiles has completed loading.
            int maxIndexY = this.CompletedRowIndex; // if all tiles are created then all tiles can be written
            if (maxIndexY < this.TileGrid.SizeY - 1)
            {
                --maxIndexY; // row n of DSM isn't fully created yet so canopy maxima models for row n - 1 cannot yet be generated without edge effects
            }
            for (; this.pendingIndexY <= maxIndexY; ++this.pendingIndexY)
            {
                for (; this.pendingIndexX < this.TileGrid.SizeX; ++this.pendingIndexX)
                {
                    TTile? nextTileCandidate = this.TileGrid[this.pendingIndexX, this.pendingIndexY];
                    if (nextTileCandidate == null)
                    {
                        // since row is completed a null cell in the virtual raster indicates there's no tile to write at this position
                        continue;
                    }

                    tileIndexX = this.pendingIndexX;
                    tileIndexY = this.pendingIndexY;
                    nextTile = nextTileCandidate;

                    // advance to next grid position
                    ++this.pendingIndexX;
                    if (this.pendingIndexX >= this.TileGrid.SizeX)
                    {
                        ++this.pendingIndexY;
                        this.pendingIndexX = 0;
                    }
                    return true;
                }

                this.pendingIndexX = 0; // reset to beginning of row, next iteration of loop will increment in y
            }

            tileIndexX = -1;
            tileIndexY = -1;
            nextTile = null;
            return false;
        }

        public bool TryReturnToRasterBandPool(RasterBandPool dataBufferPool)
        {
            return this.TryReturnToRasterBandPool(dataBufferPool, this.CompletedRowIndex);
        }

        public bool TryReturnToRasterBandPool(RasterBandPool dataBufferPool, int maxRowIndexInclusive)
        {
            if (this.rasterTileGrid == false)
            {
                return false;
            }

            // if all tiles in virtual raster have been processed then all tiles can be freed, if not then rows 0..n - 1 can be freed
            // Completion to row n within the virtual raster means processing is occuring within rows n + 1, n + 2, .... For eight way
            // neighborhoods of size 1 then the lowest row referenced by any neighborhood in current processing is thus row n.
            int maxRowFreeIndexInclusive = Int32.Min(this.CompletedRowIndex, maxRowIndexInclusive);
            if (this.CompletedRowIndex < this.TileGrid.SizeY - 1)
            {
                // row n of virtual raster isn't fully completed so row n - 1 cannot be released without likely loss of tiles still within an
                // active processing neighborhood
                --maxRowFreeIndexInclusive;
            }

            bool atLeastOneRowFreed = false;
            int maxXindex = this.TileGrid.SizeX;
            for (; this.PendingRowFreeIndex <= maxRowFreeIndexInclusive; ++this.PendingRowFreeIndex)
            {
                for (int xIndex = 0; xIndex < maxXindex; ++xIndex)
                {
                    Raster? rasterTile = this.TileGrid[xIndex, this.PendingRowFreeIndex] as Raster;
                    rasterTile?.ReturnBands(dataBufferPool);
                }
                atLeastOneRowFreed = true;
            }

            return atLeastOneRowFreed;
        }
    }
}
