using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Cmdlets
{
    public class TileStreamPosition
    {
        private readonly bool[,] isTileCompleted;

        /// <summary>
        /// Greatest x and y index of tile with continguous completion, packed for atomic updates and thread safe interaction between 
        /// <see cref="IsCompleteTo(int, int)" and <see cref="OnTileCompleted(int, int)"/>. Used for checking neighborhood completion. 
        /// X index is in the in upper 32 bits, y in the lower 32.
        /// </summary>
        /// <remarks>
        /// All tiles from [0, 0] to [completedX, completedY] have been marked complete. Due to out of order completion by threads it is likely
        /// some tiles at higher indices have also been completed.
        /// </remarks>
        protected long CompletedIndexXY { get; set; }
        protected int PendingIndexX { get; set; }
        protected int PendingIndexY { get; set; }

        /// <summary>
        /// Greatest y index of a row with contiguous completion for all of its tiles and all previous tiles. Used for releasing tiles
        /// once a row is no longer within reach of an active neighborhood.
        /// </summary>
        public int CompletedRowIndex { get; private set; }

        public Grid TileGrid { get; private init; }
        public bool TileReturnDoesNotRequirePreviousRow { get; init; }

        public TileStreamPosition(Grid tileGrid, bool[,] unpopulatedTileMap)
        {
            this.isTileCompleted = unpopulatedTileMap;
            
            this.CompletedIndexXY = Int64Extensions.Pack(-1, -1);
            this.CompletedRowIndex = -1;
            this.PendingIndexX = 0;
            this.PendingIndexY = 0;
            this.TileGrid = tileGrid;
            this.TileReturnDoesNotRequirePreviousRow = false;
        }

        public bool IsCompleteTo(int tileIndexX, int tileIndexY)
        {
            Debug.Assert((tileIndexX >= 0) && (tileIndexX < this.TileGrid.SizeX) && (tileIndexY >= 0) && (tileIndexY < this.TileGrid.SizeY));

            int completedIndexY = this.CompletedIndexXY.GetLowerInt32();
            if (completedIndexY < tileIndexY)
            {
                return false;
            }
            if (completedIndexY == tileIndexY)
            {
                int completedIndexX = this.CompletedIndexXY.GetUpperInt32();
                return tileIndexX <= completedIndexX;
            }

            Debug.Assert(this.isTileCompleted[tileIndexX, tileIndexY]); // sanity check
            return true;
        }

        public void OnTileCompleted(int tileIndexX, int tileIndexY)
        {
            this.isTileCompleted[tileIndexX, tileIndexY] = true;

            // scan to see if one or more rows of tiles has been completed since the last call
            int tileGridSizeX = this.TileGrid.SizeX;
            int tileGridSizeY = this.TileGrid.SizeY;
            int maxXindexWithContinguousCompletion = this.CompletedIndexXY.GetUpperInt32();
            int maxYindexWithContinguousCompletion = this.CompletedIndexXY.GetLowerInt32();
            int startIndexX = maxXindexWithContinguousCompletion == tileGridSizeX - 1 ? 0 : maxXindexWithContinguousCompletion + 1;
            int startIndexY = this.CompletedRowIndex + 1;
            for (int yIndex = startIndexY; yIndex < tileGridSizeY; ++yIndex)
            {
                bool rowIncomplete = false;
                for (int xIndex = startIndexX; xIndex < tileGridSizeX; ++xIndex)
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

            this.CompletedIndexXY = Int64Extensions.Pack(maxXindexWithContinguousCompletion, maxYindexWithContinguousCompletion);
        }
    }

    /// <summary>
    /// Track streaming of vector or raster tiles through memory for read or creation.
    /// </summary>
    public class TileStreamPosition<TTile> : TileStreamPosition where TTile : class
    {
        private int pendingRowReleaseIndex;

        public new GridNullable<TTile> TileGrid { get; private init; }

        public TileStreamPosition(GridNullable<TTile> tileGrid, bool[,] unpopulatedTileMap)
            : base(tileGrid, unpopulatedTileMap)
        {
            this.pendingRowReleaseIndex = 0;

            this.TileGrid = tileGrid;
        }

        public void OnTileCompleted(int tileIndexX, int tileIndexY, Action<int, int, TTile>? onTileUnreferenced)
        {
            // no external constraint on row release
            // this.CompletedRowIndex does not provide a valid upper bound as base.OnTileCompleted() has not yet been called on this
            // tile. this.TileGrid.SizeY can be used if, for some reason, an upper bound's imposed on the external constraint.
            this.OnTileCompleted(tileIndexX, tileIndexY, Int32.MaxValue, onTileUnreferenced);
        }

        /// <summary>
        /// Mark tile at the specified grid position as complete and release rows no longer needed in the tile grid.
        /// </summary>
        /// <param name="tileIndexX">x index of tile that was just completed</param>
        /// <param name="tileIndexY">y index of tile that was just completed</param>
        /// <param name="rowReleaseConstraintInclusive">an external upper bound on the greatest row number which can be released</param>
        /// <param name="onTileUnreferenced">action invoked on each tile in an unneeded grid row, can be left null if there's no action to perform</param>
        /// <returns><see cref="true"/>if this tile's completion resulted in one or more rows no longer being needed</returns>
        /// <remarks>
        /// In cases where two processing steps work with the same tiles, <paramref name="rowReleaseConstraintInclusive"/> provides an
        /// interlock to prevent the first step's <see cref="TileStreamPosition"/> from releasing tiles before the second step completes.
        /// The most common case where <paramref name="rowReleaseConstraintInclusive"/> is needed is when a tile is marked read but 
        /// creation of tiles depends on neighborhoods of read tiles. As the purpose of <paramref name="rowReleaseConstraintInclusive"/>
        /// to prevent some tiles from being released, if all tiles need to be released then callers should sweep the tile grid after 
        /// processing completes to release any remaining tile.
        /// </remarks>
        public void OnTileCompleted(int tileIndexX, int tileIndexY, int rowReleaseConstraintInclusive, Action<int, int, TTile>? onTileUnreferenced)
        {
            base.OnTileCompleted(tileIndexX, tileIndexY);
            
            int maxRowReleaseIndexInclusive = this.CompletedRowIndex; // if all tiles have been completed then all tiles can be released
            if (this.CompletedRowIndex < this.TileGrid.SizeY - 1)
            {
                // tile processing continues on rows { n, max } as virtual raster isn't completed
                if (this.TileReturnDoesNotRequirePreviousRow == false)
                {
                    // row n - 1 cannot be released without likely loss of tiles still within an active processing neighborhood
                    --maxRowReleaseIndexInclusive;
                }
            }

            if (rowReleaseConstraintInclusive < maxRowReleaseIndexInclusive)
            {
                // an external override can prevent release of all tiles
                // Most commonly, external overrides mean the last two rows (or last row if this.TileReturnDoesNotRequirePreviousRow = true)
                // will not be released. If tile completion's very unordered, for example when a limited number of tiles results in as many
                // processing threads as tiles, it's possible no tiles are released.
                maxRowReleaseIndexInclusive = rowReleaseConstraintInclusive;
            }

            if (onTileUnreferenced != null)
            {
                int maxXindex = this.TileGrid.SizeX;
                for (; this.pendingRowReleaseIndex <= maxRowReleaseIndexInclusive; ++this.pendingRowReleaseIndex)
                {
                    for (int xIndex = 0; xIndex < maxXindex; ++xIndex)
                    {
                        TTile? tile = this.TileGrid[xIndex, this.pendingRowReleaseIndex];
                        if (tile != null)
                        {
                            onTileUnreferenced(xIndex, this.pendingRowReleaseIndex, tile);
                        }
                    }
                }
            }
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
            for (; this.PendingIndexY <= maxIndexY; ++this.PendingIndexY)
            {
                for (; this.PendingIndexX < this.TileGrid.SizeX; ++this.PendingIndexX)
                {
                    TTile? nextTileCandidate = this.TileGrid[this.PendingIndexX, this.PendingIndexY];
                    if (nextTileCandidate == null)
                    {
                        // since row is completed a null cell in the virtual raster indicates there's no tile to write at this position
                        continue;
                    }

                    tileIndexX = this.PendingIndexX;
                    tileIndexY = this.PendingIndexY;
                    nextTile = nextTileCandidate;

                    // advance to next grid position
                    ++this.PendingIndexX;
                    if (this.PendingIndexX >= this.TileGrid.SizeX)
                    {
                        ++this.PendingIndexY;
                        this.PendingIndexX = 0;
                    }
                    return true;
                }

                this.PendingIndexX = 0; // reset to beginning of row, next iteration of loop will increment in y
            }

            tileIndexX = -1;
            tileIndexY = -1;
            nextTile = null;
            return false;
        }
    }
}
