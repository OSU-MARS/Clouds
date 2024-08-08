using Mars.Clouds.GdalExtensions;
using System;

namespace Mars.Clouds.Cmdlets
{
    public class TileReadWriteStreaming<TTile> : TileReadWrite where TTile : Raster
    {
        private readonly TileStreamPosition<TTile> tileReadPosition;
        private readonly TileStreamPosition<TTile> tileWritePosition;

        public int MaxTileIndex { get; private init; }
        public RasterBandPool RasterBandPool { get; private init; }
        public bool TileWriteDoesNotRequirePreviousRow { get; init; }

        public TileReadWriteStreaming(VirtualRaster<TTile> vrt, bool outputPathIsDirectory)
            : base(outputPathIsDirectory)
        {
            if (vrt.TileGrid == null)
            {
                throw new ArgumentOutOfRangeException(nameof(vrt), "Virtual raster's grid must be created before tiles can be streamed from it.");
            }

            bool[,] unpopulatedTileMap = vrt.TileGrid.GetUnpopulatedCellMap();
            bool[,] unpopulatedTileMapCopy = new bool[unpopulatedTileMap.GetLength(0), unpopulatedTileMap.GetLength(1)];
            Array.Copy(unpopulatedTileMap, unpopulatedTileMapCopy, unpopulatedTileMap.Length);
            this.tileReadPosition = new(vrt.TileGrid, unpopulatedTileMap);
            this.tileWritePosition = new(vrt.TileGrid, unpopulatedTileMapCopy);

            this.MaxTileIndex = vrt.VirtualRasterSizeInTilesX * vrt.VirtualRasterSizeInTilesY;
            this.RasterBandPool = new();
            this.TileWriteDoesNotRequirePreviousRow = false;
        }

        public int GetMaximumIndexNeighborhood8(int writeIndex)
        {
            int tileGridSizeX = this.tileReadPosition.TileGrid.SizeX;
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
            (int tileIndexX, int tileIndexY) = this.tileReadPosition.TileGrid.ToGridIndices(tileIndex);
            return this.tileReadPosition.IsCompleteTo(tileIndexX, tileIndexY);
        }

        public void OnTileRead(int tileReadIndex)
        {
            (int readTileIndexX, int readTileIndexY) = this.tileReadPosition.TileGrid.ToGridIndices(tileReadIndex);
            this.tileReadPosition.OnTileCompleted(readTileIndexX, readTileIndexY);
            int maxReadReturnRowIndex = this.tileWritePosition.CompletedRowIndex;
            if (this.TileWriteDoesNotRequirePreviousRow == false)
            {
                --maxReadReturnRowIndex;
            }
            this.tileReadPosition.TryReturnToRasterBandPool(this.RasterBandPool, maxReadReturnRowIndex);
            ++this.TilesRead;
        }

        public void OnTileWritten(int tileWriteIndex)
        {
            (int writeTileIndexX, int writeTileIndexY) = this.tileReadPosition.TileGrid.ToGridIndices(tileWriteIndex);
            this.tileWritePosition.OnTileCompleted(writeTileIndexX, writeTileIndexY);
            this.tileWritePosition.TryReturnToRasterBandPool(this.RasterBandPool);
            ++this.TilesWritten;
        }
    }
}
