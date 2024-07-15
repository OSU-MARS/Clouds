using System;

namespace Mars.Clouds.Extensions
{
    internal static class TimeSpanExtensions
    {
        public static string ToElapsedString(this TimeSpan elapsedTime)
        {
            return elapsedTime.ToString(elapsedTime.TotalHours >= 1.0 ? "hh\\:mm\\:ss" : "mm\\:ss"); 
        }
    }
}
