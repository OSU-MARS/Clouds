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
        protected Action<TSourceTile>? OnSourceTileRelease { get; private init; }
        protected TileStreamPosition<TSourceTile> TileReadPosition { get; private init; }
        protected TWritePosition TileWritePosition { get; private init; }

        public int MaxTileIndex { get; private init; }
        public RasterBandPool RasterBandPool { get; private init; }

        public TileReadWriteStreaming(GridNullable<TSourceTile> sourceGrid, bool[,] unpopulatedTileMapForRead, TWritePosition tileWritePosition, bool outputPathIsDirectory)
            : base(outputPathIsDirectory)
        {
            this.TileReadPosition = new(sourceGrid, unpopulatedTileMapForRead);
            this.TileWritePosition = tileWritePosition;

            this.OnSourceTileRelease = null;

            this.MaxTileIndex = sourceGrid.Cells; // could be elided by retaining a pointer to sourceGrid
            this.RasterBandPool = new();

            if (typeof(TSourceTile).IsSubclassOf(typeof(Raster)))
            {
                this.OnSourceTileRelease = (TSourceTile tile) =>
                {
                    Raster? raster = tile as Raster;
                    Debug.Assert(raster != null);
                    raster.ReturnBands(this.RasterBandPool);
                };
            }
        }

        public int GetMaximumIndexNeighborhood8(int writeIndex)
        {
            int tileGridSizeX = this.TileReadPosition.TileGrid.SizeX;
            int writeIndexX = writeIndex % tileGridSizeX;

            int readCompletionIndexInclusive = writeIndex + tileGridSizeX; // advance one row
            if (writeIndexX != tileGridSizeX - 1)
            {
                ++readCompletionIndexInclusive; // if not at the +x end of a row, include bishop adjacent neighbor (southeast corner, usually)
            }

            if (readCompletionIndexInclusive >= this.MaxTileIndex)
            { 
                readCompletionIndexInclusive = this.MaxTileIndex - 1; // can't require read past end of virtual raster
            }

            return readCompletionIndexInclusive;
        }

        public bool IsReadCompleteTo(int tileIndex)
        {
            (int tileIndexX, int tileIndexY) = this.TileReadPosition.TileGrid.ToGridIndices(tileIndex);
            return this.TileReadPosition.IsCompleteTo(tileIndexX, tileIndexY);
        }

        public virtual void OnTileRead(int tileReadIndexX, int tileReadIndexY)
        {
            this.TileReadPosition.OnTileCompleted(tileReadIndexX, tileReadIndexY, this.TileWritePosition.CompletedRowIndex, this.OnSourceTileRelease);
            ++this.TilesRead;
        }

        public void OnTileWritten(int tileWriteIndexX, int tileWriteIndexY)
        {
            this.TileWritePosition.OnTileCompleted(tileWriteIndexX, tileWriteIndexY);
            ++this.TilesWritten;
        }

        public bool TryEnsureRasterNeighborhoodRead<TTile>(int tileIndex, VirtualRaster<TTile> vrt, CancellationTokenSource cancellationTokenSource) where TTile : Raster
        {
            // if necessary, the outer while loop spin waits for other threads to complete neighborhood read
            int maxNeighborhoodIndex = this.GetMaximumIndexNeighborhood8(tileIndex);
            if (this.IsReadCompleteTo(maxNeighborhoodIndex))
            {
                return true; // nothing to do
            }

            for (int tileReadIndex = this.GetNextTileReadIndexThreadSafe(); tileReadIndex < this.MaxTileIndex; tileReadIndex = this.GetNextTileReadIndexThreadSafe())
            {
                TTile? tileToRead = vrt[tileReadIndex];
                if (tileToRead == null)
                {
                    continue;
                }

                // must load tile at given index even if it's beyond the necessary neighborhood
                // Otherwise some tiles would not get loaded.
                if (this.RasterBandPool.FloatPool.Count > 0)
                {
                    lock (this.RasterBandPool)
                    {
                        tileToRead.TryTakeOwnershipOfDataBuffers(this.RasterBandPool);
                    }
                }

                tileToRead.ReadBandData();

                (int tileReadIndexX, int tileReadIndexY) = vrt.ToGridIndices(tileReadIndex);
                lock (this)
                {
                    this.OnTileRead(tileReadIndexX, tileReadIndexY);
                }
                if (cancellationTokenSource.IsCancellationRequested)
                {
                    return false;
                }
                if (this.IsReadCompleteTo(maxNeighborhoodIndex))
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
