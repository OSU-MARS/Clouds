using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using System;
using System.Diagnostics;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    internal static class TileReadWriteStreaming
    {
        public static readonly TimeSpan NeighborhoodReadCompletionPollInterval = TimeSpan.FromSeconds(0.1);

        public static TileReadWriteStreaming<TSourceTile, TileStreamPosition> Create<TSourceTile>(GridNullable<TSourceTile> sourceGrid, bool outputPathIsDirectory) where TSourceTile : class
        {
            bool[,] unpopulatedTileMapForRead = sourceGrid.GetUnpopulatedCellMap();
            bool[,] unpopulatedTileMapForWrite = ArrayExtensions.Copy(unpopulatedTileMapForRead);
            return new(sourceGrid, unpopulatedTileMapForRead, new(sourceGrid, unpopulatedTileMapForWrite), outputPathIsDirectory);
        }
    }

    /// <summary>
    /// Stream input raster tiles through memory when the output tiles require neighborhoods of input tiles.
    /// </summary>
    public class TileReadWriteStreaming<TSourceTile, TWritePosition> : TileReadWrite 
        where TSourceTile : class
        where TWritePosition : TileStreamPosition
    {
        protected TileStreamPosition<TSourceTile> TileReadPosition { get; private init; }
        protected TWritePosition TileWritePosition { get; private init; }

        public int MaxTileIndex { get; private init; }
        public RasterBandPool WriteBandPool { get; private init; }

        public TileReadWriteStreaming(GridNullable<TSourceTile> sourceGrid, bool[,] unpopulatedTileMapForRead, TWritePosition tileWritePosition, bool outputPathIsDirectory)
            : base(outputPathIsDirectory)
        {
            this.TileReadPosition = new(sourceGrid, unpopulatedTileMapForRead);
            this.TileWritePosition = tileWritePosition;

            this.MaxTileIndex = sourceGrid.Cells; // could be elided by retaining a pointer to sourceGrid
            this.WriteBandPool = new();
        }

        public int GetMaximumIndexNeighborhood8(int tileCreateOrWriteIndex)
        {
            int tileGridSizeX = this.TileReadPosition.TileGrid.SizeX;
            int writeIndexX = tileCreateOrWriteIndex % tileGridSizeX;

            int readCompletionIndexInclusive = tileCreateOrWriteIndex + tileGridSizeX; // advance one row
            if (writeIndexX != tileGridSizeX - 1)
            {
                ++readCompletionIndexInclusive; // if not at the +x end of a row, include bishop adjacent neighbor (southeast corner, usually)
            }

            if (readCompletionIndexInclusive >= this.MaxTileIndex)
            {
                readCompletionIndexInclusive = this.MaxTileIndex - 1; // can't require read past end of virtual raster's tile grid
            }

            return readCompletionIndexInclusive;
        }

        public bool IsReadCompleteTo(int tileIndex)
        {
            (int tileIndexX, int tileIndexY) = this.TileReadPosition.TileGrid.ToGridIndices(tileIndex);
            return this.TileReadPosition.IsCompleteTo(tileIndexX, tileIndexY);
        }

        protected virtual void OnSourceTileRead(int tileReadIndexX, int tileReadIndexY)
        {
            // nothing to do by default
        }

        protected virtual void OnSourceTileUnreferenced(int tileReadIndexX, int tileReadIndexY, TSourceTile sourceTile)
        {
            if (typeof(TSourceTile).IsSubclassOf(typeof(Raster)))
            {
                Raster? sourceRaster = sourceTile as Raster;
                Debug.Assert(sourceRaster != null);
                sourceRaster.ReturnBandData(this.WriteBandPool);
            }
        }

        public void OnTileRead(int tileReadIndexX, int tileReadIndexY)
        {
            int maxReleasableRowIndex = this.TileWritePosition.CompletedRowIndex; // inclusive
            if (this.TileWritePosition.TileReturnDoesNotRequirePreviousRow == false)
            {
                --maxReleasableRowIndex;
            }
            this.TileReadPosition.OnTileCompleted(tileReadIndexX, tileReadIndexY, maxReleasableRowIndex, this.OnSourceTileUnreferenced);
            ++this.TilesRead;
        }

        public void OnTileWritten(int tileWriteIndexX, int tileWriteIndexY)
        {
            this.TileWritePosition.OnTileCompleted(tileWriteIndexX, tileWriteIndexY);
            ++this.TilesWritten;
        }

        public bool TryEnsureNeighborhoodRead<TRasterSourceTile>(int tileIndex, VirtualRaster<TRasterSourceTile> vrtMatchingReadPosition, CancellationTokenSource cancellationTokenSource) where TRasterSourceTile : Raster
        {
            // if necessary, the outer while loop spin waits for other threads to complete neighborhood read
            int maxNeighborhoodIndex = this.GetMaximumIndexNeighborhood8(tileIndex);
            if (this.IsReadCompleteTo(maxNeighborhoodIndex))
            {
                return true; // nothing to do
            }

            for (int tileReadIndex = this.GetNextTileReadIndexThreadSafe(); tileReadIndex < this.MaxTileIndex; tileReadIndex = this.GetNextTileReadIndexThreadSafe())
            {
                TRasterSourceTile? tileToRead = vrtMatchingReadPosition[tileReadIndex];
                if (tileToRead == null)
                {
                    continue;
                }

                // must load tile at given index even if it's beyond the necessary neighborhood
                // Otherwise some tiles would not get loaded.
                lock (this)
                {
                    tileToRead.TryTakeOwnershipOfDataBuffers(this.WriteBandPool);
                }

                tileToRead.ReadBandData();

                // for some derived classes it might be useful to pass tileToRead here
                // However, this is impractical as TRasterSourceTile is a special case of TSourceTile necessary for this class
                // to support .las (and other non-raster) source tiles.
                (int tileReadIndexX, int tileReadIndexY) = vrtMatchingReadPosition.ToGridIndices(tileReadIndex);
                this.OnSourceTileRead(tileReadIndexX, tileReadIndexY);

                lock (this)
                {
                    this.OnTileRead(tileReadIndexX, tileReadIndexY);
                }
                if (cancellationTokenSource.IsCancellationRequested)
                {
                    return false;
                }
                else if (this.IsReadCompleteTo(maxNeighborhoodIndex))
                {
                    return true;
                }
            }

            while ((this.IsReadCompleteTo(maxNeighborhoodIndex) == false) && (cancellationTokenSource.IsCancellationRequested == false))
            {
                // all tiles have pending reads, nothing to do but block until remaining read threads complete
                Thread.Sleep(TileReadWriteStreaming.NeighborhoodReadCompletionPollInterval);
            }

            return cancellationTokenSource.IsCancellationRequested == false;
        }
    }
}
