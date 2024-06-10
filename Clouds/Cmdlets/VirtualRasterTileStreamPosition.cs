using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using System.Diagnostics;

namespace Mars.Clouds.Cmdlets
{
    /// <summary>
    /// Strack streaming of virtual raster tiles through memory for read or creation.
    /// </summary>
    public class VirtualRasterTileStreamPosition<TTile> where TTile : Raster
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
        private int pendingRowFreeIndex;

        /// <summary>
        /// Greatest y index of a row with contiguous completion for all of its tiles and all previous tiles. Used for releasing tiles
        /// once a row is no longer within reach of an active neighborhood.
        /// </summary>
        public int CompletedRowIndex { get; private set; }

        public VirtualRaster<TTile> Vrt { get; private init; }

        public VirtualRasterTileStreamPosition(VirtualRaster<TTile> vrt)
            : this(vrt, vrt.GetUnpopulatedTileMap())
        {
        }

        public VirtualRasterTileStreamPosition(VirtualRaster<TTile> vrt, bool[,] unpopulatedTileMap)
        {
            this.completedIndexXY = Int64Extensions.Pack(-1, -1);
            this.CompletedRowIndex = -1;
            this.isTileCompleted = unpopulatedTileMap;
            this.pendingRowFreeIndex = 0;

            this.Vrt = vrt;
        }

        public bool IsCompleteTo(int tileIndexX, int tileIndexY)
        {
            Debug.Assert((tileIndexX >= 0) && (tileIndexX < this.Vrt.VirtualRasterSizeInTilesX) && (tileIndexY >= 0) && (tileIndexY < this.Vrt.VirtualRasterSizeInTilesY));

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

            // scan to see if creation of one or more rows of DSM tiles has been completed since the last call
            // Similar to code in TileReadCreateWriteStreaming.TryGetNextTileWriteIndex().
            int vrtSizeInTilesX = this.Vrt.VirtualRasterSizeInTilesX;
            int vrtSizeInTilesY = this.Vrt.VirtualRasterSizeInTilesY;
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

        public bool TryReturnTilesToObjectPool(ObjectPool<TTile> tilePool)
        {
            // if all tiles in virtual raster have been processed then all tiles can be freed, if not then rows 0..n - 1 can be freed
            // Completion to row n within the virtual raster means processing is occuring within rows n + 1, n + 2, .... For eight way
            // neighborhoods of size 1 then the lowest row referenced by any neighborhood in current processing is thus row n.
            int maxRowFreeIndexInclusive = this.CompletedRowIndex;
            if (this.CompletedRowIndex < this.Vrt.VirtualRasterSizeInTilesY - 1)
            {
                // row n of DSM isn't fully written so row n - 1 cannot be released without likely loss of tiles still within an
                // active processing neighborhood
                --maxRowFreeIndexInclusive;
            }

            bool rowsFreed = false;
            for (; this.pendingRowFreeIndex <= maxRowFreeIndexInclusive; ++this.pendingRowFreeIndex)
            {
                this.Vrt.ReturnRowToObjectPool(this.pendingRowFreeIndex, tilePool);
                rowsFreed = true;
            }

            return rowsFreed;
        }
    }
}
