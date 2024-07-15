using System.Diagnostics;

namespace Mars.Clouds.Extensions
{
    internal static class StopwatchExtensions
    {
        public static string ToElapsedString(this Stopwatch stopwatch)
        {
            return stopwatch.Elapsed.ToElapsedString();
        }
    }
}
