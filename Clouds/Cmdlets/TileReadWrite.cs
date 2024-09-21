using Mars.Clouds.Las;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    /// <summary>
    /// Track input tile reads and output tile writes when each output tile uses only data in the corresponding input tile.
    /// </summary>
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

        public virtual string GetLasReadTileWriteStatusDescription(LasTileGrid lasGrid, int activeReadThreads, int totalThreads)
        {
            string status = this.TilesRead + (this.TilesRead == 1 ? " point cloud tile read, " : " point cloud tiles read, ") +
                            this.TilesWritten + " of " + lasGrid.NonNullCells + " tiles written (" + totalThreads + 
                            (totalThreads == 1 ? " thread, " : " threads, ") + activeReadThreads + " reading)...";
            return status;
        }
    }
}
