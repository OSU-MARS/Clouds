using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    /// <summary>
    /// Track input tile reads for a workload whose output is either not tiled (monolithic) or modifies tiles in place rather than
    /// writing separate tiles.
    /// </summary>
    public class TileRead
    {
        private int tileReadIndex;
        private int tilesRead;

        public TileRead()
        {
            this.tileReadIndex = -1;
            this.tilesRead = 0;
        }

        public int TileReadIndex
        {
            get { return this.tileReadIndex; }
        }

        public int TilesRead
        {
            get { return this.tilesRead; }
            set { this.tilesRead = value; }
        }

        public int GetNextTileReadIndexThreadSafe()
        {
            return Interlocked.Increment(ref this.tileReadIndex);
        }

        public int IncrementTilesReadThreadSafe()
        {
            return Interlocked.Increment(ref this.tilesRead);
        }
    }
}
