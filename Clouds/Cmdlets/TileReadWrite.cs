using System.Collections.Concurrent;

namespace Mars.Clouds.Cmdlets
{
    public class TileReadWrite : TileRead
    {
        public long CellsWritten { get; set; } // 32 integer will overflow on large jobs
        public bool OutputPathIsDirectory { get; private init; }
        public int TileSizeX { get; private init; }
        public int TileSizeY { get; private init; }
        public int TilesWritten { get; set; }

        public TileReadWrite(int tileSizeX, int tileSizeY, bool outputPathIsDirectory)
        {
            this.CellsWritten = 0;
            this.OutputPathIsDirectory = outputPathIsDirectory;
            this.TileSizeX = tileSizeX;
            this.TileSizeY = tileSizeY;
            this.TilesWritten = 0;
        }
    }

    public class TileReadWrite<TTile> : TileReadWrite
    {
        public BlockingCollection<(string, TTile)> LoadedTiles { get; private init; }

        public TileReadWrite(int maxTiles, int tileSizeX, int tileSizeY, bool outputPathIsDirectory)
            : base(tileSizeX, tileSizeY, outputPathIsDirectory)
        {
            this.LoadedTiles = new(maxTiles);
        }
    }
}
