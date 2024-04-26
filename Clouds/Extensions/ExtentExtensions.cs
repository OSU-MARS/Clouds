using Mars.Clouds.GdalExtensions;

namespace Mars.Clouds.Extensions
{
    internal static class ExtentExtensions
    {
        public static string GetExtentString(this Extent extent)
        {
            return extent.XMin + ", " + extent.XMax + ", " + extent.YMin + ", " + extent.YMax;
        }
    }
}
