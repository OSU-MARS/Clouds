using System.Diagnostics;

namespace Mars.Clouds.Extensions
{
    internal static class StopwatchExtensions
    {
        public static string ToElapsedString(this Stopwatch stopwatch)
        {
            return stopwatch.Elapsed.ToString(stopwatch.Elapsed.TotalHours >= 1.0 ? "hh\\:mm\\:ss" : "mm\\:ss"); 
        }
    }
}
