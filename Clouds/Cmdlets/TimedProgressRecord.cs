using System;
using System.Diagnostics;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    public class TimedProgressRecord : ProgressRecord
    {
        public Stopwatch Stopwatch { get; private init; }

        public TimedProgressRecord(string cmdlet, string initialStatus)
            : base(0, cmdlet, initialStatus)
        {
            this.Stopwatch = Stopwatch.StartNew();
        }

        public void Update(int completed, int total)
        {
            Debug.Assert((completed >= 0) && (total > 0));

            double fractionComplete = (double)completed / (double)total;
            this.PercentComplete = Int32.Min((int)(100.0F * fractionComplete), 100); // PSArgumentOutOfRangeException on 101+%
            this.SecondsRemaining = fractionComplete > 0.0 ? (int)Double.Round(this.Stopwatch.Elapsed.TotalSeconds * (1.0 / fractionComplete - 1.0)) : 0;
        }
    }
}
