using System.Collections.Concurrent;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    public class TileReadWrite : TileRead
    {
        private int tileWriteIndex;

        public bool OutputPathIsDirectory { get; private init; }
        public int TilesWritten { get; set; }

        public TileReadWrite(bool outputPathIsDirectory)
        {
            this.tileWriteIndex = -1;

            this.OutputPathIsDirectory = outputPathIsDirectory;
            this.TilesWritten = 0;
        }

        public int GetNextTileWriteIndex()
        {
            return Interlocked.Increment(ref this.tileWriteIndex);
        }
    }

    public class TileReadWrite<TTile> : TileReadWrite
    {
        public long CellsWritten { get; set; } // 32 integer will overflow on large jobs
        public BlockingCollection<(string Name, TTile Tile)> LoadedTiles { get; private init; }
        public int TileSizeX { get; private init; }
        public int TileSizeY { get; private init; }

        public TileReadWrite(int maxSimultaneouslyLoadedTiles, int tileSizeX, int tileSizeY, bool outputPathIsDirectory)
            : base(outputPathIsDirectory)
        {
            this.CellsWritten = 0;
            this.LoadedTiles = new(maxSimultaneouslyLoadedTiles); // FIFO as ConcurrentQueue is the default collection
            this.TileSizeX = tileSizeX;
            this.TileSizeY = tileSizeY;
        }
    }
}
