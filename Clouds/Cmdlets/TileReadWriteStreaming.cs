using Mars.Clouds.GdalExtensions;

namespace Mars.Clouds.Cmdlets
{
    public class TileReadWriteStreaming<TTile> : TileReadWrite where TTile : Raster
    {
        private readonly VirtualRasterTileStreamPosition<TTile> tileReadPosition;
        private readonly VirtualRasterTileStreamPosition<TTile> tileWritePosition;

        public int MaxTileIndex { get; private init; }
        public ObjectPool<TTile> TilePool { get; private init; }

        public TileReadWriteStreaming(VirtualRaster<TTile> vrt, bool outputPathIsDirectory)
            : base(outputPathIsDirectory)
        {
            this.tileReadPosition = new(vrt);
            this.tileWritePosition = new(vrt);

            this.MaxTileIndex = vrt.VirtualRasterSizeInTilesX * vrt.VirtualRasterSizeInTilesY;
            this.TilePool = new();
        }

        public int GetMaximumIndexNeighborhood8(int writeIndex)
        {
            int vrtSizeInTilesX = this.tileReadPosition.Vrt.VirtualRasterSizeInTilesX;
            int readCompletionIndexInclusive = writeIndex + vrtSizeInTilesX; // advance one row
            if (writeIndex % vrtSizeInTilesX != 0)
            {
                ++readCompletionIndexInclusive; // if not at the +x end of a row, advance one neighbor
            }

            if (readCompletionIndexInclusive >= this.MaxTileIndex)
            { 
                readCompletionIndexInclusive = this.MaxTileIndex - 1; // can't require read past end of virtual raster
            }

            return readCompletionIndexInclusive;
        }

        public bool IsReadCompleteTo(int tileIndex)
        {
            (int tileIndexX, int tileIndexY) = this.tileReadPosition.Vrt.ToGridIndices(tileIndex);
            return this.tileReadPosition.IsCompleteTo(tileIndexX, tileIndexY);
        }

        public void OnTileRead(int tileReadIndex)
        {
            (int readTileIndexX, int readTileIndexY) = this.tileReadPosition.Vrt.ToGridIndices(tileReadIndex);
            this.tileReadPosition.OnTileCompleted(readTileIndexX, readTileIndexY);
            ++this.TilesRead;
        }

        public void OnTileWritten(int tileWriteIndex)
        {
            (int writeTileIndexX, int writeTileIndexY) = this.tileReadPosition.Vrt.ToGridIndices(tileWriteIndex);
            this.tileWritePosition.OnTileCompleted(writeTileIndexX, writeTileIndexY);
            this.tileWritePosition.TryReturnTilesToObjectPool(this.TilePool);
            ++this.TilesWritten;
        }
    }
}
