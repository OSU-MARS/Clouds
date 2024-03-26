using System.Diagnostics;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    public class TileRead
    {
        private int tileReadIndex;

        public CancellationTokenSource CancellationTokenSource { get; private init; }
        public Stopwatch Stopwatch { get; private init; }
        public int TilesLoaded { get; set; }

        public TileRead()
        {
            this.tileReadIndex = -1;

            this.CancellationTokenSource = new();
            this.Stopwatch = Stopwatch.StartNew();
            this.TilesLoaded = 0;
        }

        public int GetNextTileReadIndexThreadSafe()
        {
            return Interlocked.Increment(ref this.tileReadIndex);
        }
    }
}
