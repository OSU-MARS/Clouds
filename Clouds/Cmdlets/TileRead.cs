using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    public class TileRead
    {
        private int tileReadIndex;
        private int tilesRead;

        public CancellationTokenSource CancellationTokenSource { get; private init; }

        public TileRead()
        {
            this.tileReadIndex = -1;
            this.tilesRead = 0;

            this.CancellationTokenSource = new();
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
