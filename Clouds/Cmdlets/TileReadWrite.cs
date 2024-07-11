using Mars.Clouds.Las;
using System.Collections.Concurrent;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    public class TileReadWrite : TileRead
    {
        private int tilesWritten;
        private int tileWriteIndex;

        public bool OutputPathIsDirectory { get; private init; }

        public TileReadWrite(bool outputPathIsDirectory)
        {
            this.tilesWritten = 0;
            this.tileWriteIndex = -1;

            this.OutputPathIsDirectory = outputPathIsDirectory;
        }

        public int TilesWritten
        {
            get { return this.tilesWritten; }
            set { this.tilesWritten = value; }
        }

        public int GetNextTileWriteIndexThreadSafe()
        {
            return Interlocked.Increment(ref this.tileWriteIndex);
        }

        public virtual string GetLasReadTileWriteStatusDescription(LasTileGrid lasGrid)
        {
            string status = this.TilesRead + (this.TilesRead == 1 ? " point cloud tile read, " : " point cloud tiles read, ") +
                            this.TilesWritten + " of " + lasGrid.NonNullCells + " tiles written...";
            return status;
        }

        public string GetLasReadTileWriteStatusDescription(LasTileGrid lasGrid, int activeReadThreads, int totalThreads)
        {
            string status = this.TilesRead + (this.TilesRead == 1 ? " point cloud tile read, " : " point cloud tiles read, ") +
                            this.TilesWritten + " of " + lasGrid.NonNullCells + " tiles written (" + totalThreads + 
                            (totalThreads == 1 ? "thread, " : " threads, ") + activeReadThreads + " reading)...";
            return status;
        }
        
    }

    public class TileReadWrite<TTile> : TileReadWrite
    {
        public long CellsWritten { get; set; } // 32 integer will overflow on large jobs
        public BlockingCollection<(string Name, TTile Tile)> LoadedTiles { get; private init; }

        public TileReadWrite(int maxSimultaneouslyLoadedTiles, bool outputPathIsDirectory)
            : base(outputPathIsDirectory)
        {
            this.CellsWritten = 0;
            this.LoadedTiles = new(maxSimultaneouslyLoadedTiles); // FIFO as ConcurrentQueue is the default collection
        }

        public int AddLoadedTileThreadSafe(string tileName, TTile tile)
        {
            this.LoadedTiles.Add((tileName, tile));
            return this.IncrementTilesReadThreadSafe();
        }
    }
}
