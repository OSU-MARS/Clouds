using System.Collections.Concurrent;

namespace Mars.Clouds.Cmdlets
{
    public class TileReadWrite : TileRead
    {
        public bool OutputPathIsDirectory { get; private init; }
        public int TilesWritten { get; set; }

        public TileReadWrite(bool outputPathIsDirectory)
        {
            this.OutputPathIsDirectory = outputPathIsDirectory;
            this.TilesWritten = 0;
        }
    }

    public class TileReadWrite<TTile> : TileReadWrite
    {
        public long CellsWritten { get; set; } // 32 integer will overflow on large jobs
        public BlockingCollection<(string, TTile)> LoadedTiles { get; private init; }
        public int TileSizeX { get; private init; }
        public int TileSizeY { get; private init; }

        public TileReadWrite(int maxSimultaneouslyLoadedTiles, int tileSizeX, int tileSizeY, bool outputPathIsDirectory)
            : base(outputPathIsDirectory)
        {
            this.CellsWritten = 0;
            this.LoadedTiles = new(maxSimultaneouslyLoadedTiles);
            this.TileSizeX = tileSizeX;
            this.TileSizeY = tileSizeY;
        }
    }
}
