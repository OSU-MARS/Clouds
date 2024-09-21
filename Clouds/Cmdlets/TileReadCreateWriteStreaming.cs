using System;
using System.Diagnostics.CodeAnalysis;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Extensions;
using System.Threading;
using System.Collections.Generic;

namespace Mars.Clouds.Cmdlets
{
    public static class TileReadCreateWriteStreaming
    {
        public static TileReadCreateWriteStreaming<TSourceGrid, TSourceTile, TCreatedTile> Create<TSourceGrid, TSourceTile, TCreatedTile>(GridNullable<TSourceTile> sourceGrid, VirtualRaster<TCreatedTile> destinationVrt, bool outputPathIsDirectory, bool bypassOutputRasterWriteToDisk, bool compressRasters)
            where TSourceGrid : GridNullable<TSourceTile>
            where TSourceTile : class
            where TCreatedTile : Raster
        {
            if ((sourceGrid.SizeX != destinationVrt.VirtualRasterSizeInTilesX) || (sourceGrid.SizeY != destinationVrt.VirtualRasterSizeInTilesY))
            {
                throw new ArgumentOutOfRangeException(nameof(destinationVrt), "Source grid and destination virtual raster must be the same size. The source grid is (" + sourceGrid.SizeX + ", " + sourceGrid.SizeY + ") tiles and the virtual raster is (" + destinationVrt.VirtualRasterSizeInTilesX + ", " + destinationVrt.VirtualRasterSizeInTilesY + ").");
            }
            if (destinationVrt.TileGrid == null)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationVrt), "Virtual raster's grid must be created before tiles can be streamed from it.");
            }

            bool[,] unpopulatedTileMapForRead = sourceGrid.GetUnpopulatedCellMap();
            bool[,] unpopulatedTileMapForCreate = ArrayExtensions.Copy(unpopulatedTileMapForRead);
            bool[,] unpopulatedTileMapForWrite = ArrayExtensions.Copy(unpopulatedTileMapForRead);
            return new TileReadCreateWriteStreaming<TSourceGrid, TSourceTile, TCreatedTile>(sourceGrid, unpopulatedTileMapForRead, destinationVrt.TileGrid, unpopulatedTileMapForCreate, unpopulatedTileMapForWrite, outputPathIsDirectory)
            {
                BypassOutputRasterWriteToDisk = bypassOutputRasterWriteToDisk,
                CompressRasters = compressRasters
            };
        }
    }

    /// <summary>
    /// Stream input raster tiles through memory when tile creation requires neighborhoods of input tiles and tile writing requires a neighborhood of completed tiles.
    /// </summary>
    public class TileReadCreateWriteStreaming<TSourceGrid, TSourceTile, TCreatedTile> : TileReadWriteStreaming<TSourceTile, TileStreamPosition<TCreatedTile>>
        where TSourceGrid : GridNullable<TSourceTile>
        where TSourceTile : class
        where TCreatedTile : Raster
    {
        private readonly TileStreamPosition<TCreatedTile> tileCreatePosition;

        public bool BypassOutputRasterWriteToDisk { get; init; }
        public long CellsWritten { get; set; } // 32 bit integer overflows on moderate to large datasets
        public bool CompressRasters { get; init; }
        public int TilesCreated { get; set; }
        public bool TileCreationDoesNotRequirePreviousRow { get; init; }
        public int TileWritesInitiated { get; set; }

        public TileReadCreateWriteStreaming(GridNullable<TSourceTile> sourceGrid, bool[,] unpopulatedTileMapForRead, GridNullable<TCreatedTile> destinationGrid, bool[,] unpopulatedTileMapForCreate, bool[,] unpopulatedTileMapForWrite, bool outputPathIsDirectory)
            : base(sourceGrid, unpopulatedTileMapForRead, new TileStreamPosition<TCreatedTile>(destinationGrid, unpopulatedTileMapForWrite), outputPathIsDirectory)
        {
            this.tileCreatePosition = new(destinationGrid, unpopulatedTileMapForCreate);

            this.BypassOutputRasterWriteToDisk = false;
            this.CellsWritten = 0;
            this.CompressRasters = false;
            this.TilesCreated = 0;
            this.TileCreationDoesNotRequirePreviousRow = false;
            this.TileWritesInitiated = 0;
        }

        public void OnTileCreated(int tileCreateIndexX, int tileCreateIndexY)
        {
            this.tileCreatePosition.OnTileCompleted(tileCreateIndexX, tileCreateIndexY); // created raster's bands are returned after write
            ++this.TilesCreated;
        }

        public override void OnTileRead(int tileReadIndexX, int tileReadIndexY)
        {
            // return of source tiles must be restricted by destination tile creation
            // If left unconstrained, source tiles can be returned to pool before all referencing destination tiles are created.
            int maxReadReturnRowIndex = this.tileCreatePosition.CompletedRowIndex;
            if (this.TileCreationDoesNotRequirePreviousRow == false)
            {
                --maxReadReturnRowIndex;
            }
            this.TileReadPosition.OnTileCompleted(tileReadIndexX, tileReadIndexY, maxReadReturnRowIndex, this.OnSourceTileRelease);
            ++this.TilesRead;
        }

        protected virtual void OnTileWrite(int tileWriteIndexX, int tileWriteIndexY, TCreatedTile tileToWrite)
        {
            // no default action
        }

        public void OnTileWritten(int tileWriteIndexX, int tileWriteIndexY, TCreatedTile tileWritten)
        {
            this.TileWritePosition.OnTileCompleted(tileWriteIndexX, tileWriteIndexY, (tile) => tile.ReturnBands(this.RasterBandPool));
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
