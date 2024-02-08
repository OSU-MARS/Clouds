using System.Diagnostics;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    public class TileRead
    {
        public CancellationTokenSource CancellationTokenSource { get; private init; }
        public Stopwatch Stopwatch { get; private init; }
        public int TilesLoaded { get; set; }

        public TileRead()
        {
            this.CancellationTokenSource = new();
            this.Stopwatch = Stopwatch.StartNew();
            this.TilesLoaded = 0;
        }
    }
}
