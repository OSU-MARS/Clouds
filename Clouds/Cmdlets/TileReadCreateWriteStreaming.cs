using System.Diagnostics.CodeAnalysis;
using Mars.Clouds.GdalExtensions;
using System.Threading;
using System.Collections.Generic;
using System;

namespace Mars.Clouds.Cmdlets
{
    /// <summary>
    /// Stream input raster tiles through memory when tile creation requires neighborhoods of input tiles and tile writing requires a neighborhood of completed tiles.
    /// </summary>
    public class TileReadCreateWriteStreaming<TSourceGrid, TSourceTile, TCreatedTile> : TileReadWriteStreaming<TSourceTile, TileStreamPosition<TCreatedTile>>
        where TSourceGrid : GridNullable<TSourceTile>
        where TSourceTile : class
        where TCreatedTile : Raster
    {
        private int tileCreateIndex;
        private readonly TileStreamPosition<TCreatedTile> tileCreatePosition;

        public bool BypassOutputRasterWriteToDisk { get; init; }
        public long CellsWritten { get; set; } // 32 bit integer overflows on moderate to large datasets
        public bool CompressRasters { get; init; }
        public int TilesCreated { get; set; }
        public bool TileCreationDoesNotRequirePreviousRow { get; init; }
        public int TileWritesInitiated { get; set; }

        protected TileReadCreateWriteStreaming(GridNullable<TSourceTile> sourceGrid, bool[,] unpopulatedTileMapForRead, GridNullable<TCreatedTile> destinationGrid, bool[,] unpopulatedTileMapForCreate, bool[,] unpopulatedTileMapForWrite, bool outputPathIsDirectory)
            : base(sourceGrid, unpopulatedTileMapForRead, new TileStreamPosition<TCreatedTile>(destinationGrid, unpopulatedTileMapForWrite), outputPathIsDirectory)
        {
            this.tileCreateIndex = -1;
            this.tileCreatePosition = new(destinationGrid, unpopulatedTileMapForCreate);

            this.BypassOutputRasterWriteToDisk = false;
            this.CellsWritten = 0;
            this.CompressRasters = false;
            this.TilesCreated = 0;
            this.TileCreationDoesNotRequirePreviousRow = false;
            this.TileWritesInitiated = 0;
        }

        public int GetNextTileCreateIndexThreadSafe()
        {
            return Interlocked.Increment(ref this.tileCreateIndex);
        }

        protected virtual void OnCreatedTileUnreferenced(int tileCreateIndexX, int tileCreateIndexY, TCreatedTile tile) 
        {
            tile.ReturnBands(this.RasterBandPool);
        }

        protected override void OnSourceTileRead(int tileReadIndexX, int tileReadIndexY)
        {
            // return of source tiles must be restricted by destination tile creation
            // If left unconstrained, source tiles can be returned to pool before all referencing destination tiles are created.
            int maxReadReturnRowIndex = this.tileCreatePosition.CompletedRowIndex;
            if (this.TileCreationDoesNotRequirePreviousRow == false)
            {
                --maxReadReturnRowIndex;
            }
            this.TileReadPosition.OnTileCompleted(tileReadIndexX, tileReadIndexY, maxReadReturnRowIndex, this.OnSourceTileUnreferenced);
            ++this.TilesRead;
        }

        public void OnTileCreated(int tileCreateIndexX, int tileCreateIndexY)
        {
            this.tileCreatePosition.OnTileCompleted(tileCreateIndexX, tileCreateIndexY); // created raster's bands are returned after write
            ++this.TilesCreated;
        }

        protected virtual void OnTileWrite(int tileWriteIndexX, int tileWriteIndexY, TCreatedTile tileToBeWritten)
        {
            // nothing to do by default
        }

        public void OnTileWritten(int tileWriteIndexX, int tileWriteIndexY, TCreatedTile tileWritten)
        {
            this.TileWritePosition.OnTileCompleted(tileWriteIndexX, tileWriteIndexY, this.OnCreatedTileUnreferenced);
            this.CellsWritten += tileWritten.Cells;
            ++this.TilesWritten;
        }

        public bool TryGetNextTileToWrite(out int tileWriteIndexX, out int tileWriteIndexY, [NotNullWhen(true)] out TCreatedTile? tileToWrite)
        {
            return this.tileCreatePosition.TryGetNextTile(out tileWriteIndexX, out tileWriteIndexY, out tileToWrite);
        }

        public void TryWriteCompletedTiles(CancellationTokenSource cancellationTokenSource, GridNullable<List<RasterBandStatistics>>? bandStatisticsByTile)
        {
            bool noTilesAvailableForWrite = false;
            while (noTilesAvailableForWrite == false)
            {
                // write as many tiles as are available for completion
                TCreatedTile? tileToWrite = null;
                int tileWriteIndexX = -1;
                int tileWriteIndexY = -1;
                lock (this)
                {
                    if (this.TryGetNextTileToWrite(out tileWriteIndexX, out tileWriteIndexY, out tileToWrite))
                    {
                        this.OnTileWrite(tileWriteIndexX, tileWriteIndexY, tileToWrite);
                        ++this.TileWritesInitiated;
                    }
                    else
                    {
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            return;
                        }
                        noTilesAvailableForWrite = true;
                    }
                }
                if (tileToWrite != null)
                {
                    if (this.BypassOutputRasterWriteToDisk == false)
                    {
                        tileToWrite.Write(tileToWrite.FilePath, this.CompressRasters);
                    }
                    if (bandStatisticsByTile != null)
                    {
                        bandStatisticsByTile[tileWriteIndexX, tileWriteIndexY] = tileToWrite.GetBandStatistics();
                    }
                    lock (this)
                    {
                        // mark tile as written even when NoWrite is set so that virtual raster completion's updated and the tile's returned to the object pool
                        // Since OnTileWritten() returns completed tiles to the DSM object pool the lock taken here must be on the
                        // same object as when tiles are requested from the pool.
                        this.OnTileWritten(tileWriteIndexX, tileWriteIndexY, tileToWrite);
                    }
                }

                if (cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
