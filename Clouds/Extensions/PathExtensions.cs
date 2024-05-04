using System.IO;

namespace Mars.Clouds.Extensions
{
    internal class PathExtensions
    {
        public static string ReplaceExtension(string path, string newExtension)
        {
            string newFileName = Path.GetFileNameWithoutExtension(path) + newExtension;
            string? directoryPath = Path.GetDirectoryName(path);
            if (directoryPath == null)
            {
                return newFileName;
            }

            return Path.Combine(directoryPath, newFileName);
        }
    }
}
