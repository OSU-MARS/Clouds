using System;
using System.IO;

namespace Mars.Clouds.Extensions
{
    internal static class Tile
    {
        public static string GetName(string tileFilePath)
        {
            string? tileName = Path.GetFileNameWithoutExtension(tileFilePath);
            if (String.IsNullOrWhiteSpace(tileName))
            {
                throw new NotSupportedException("Tile name '" + tileName + "' is null or whitespace. Could not extract file name from path '" + tileFilePath + "'.");
            }

            return tileName;
        }
    }
}
