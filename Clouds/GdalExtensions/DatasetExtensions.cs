using OSGeo.GDAL;
using System;

namespace Mars.Clouds.GdalExtensions
{
    internal static class DatasetExtensions
    {
        public static string GetFirstFile(this Dataset dataset)
        {
            string[] sourceFiles = dataset.GetFileList();
            if (sourceFiles.Length < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(dataset), "Dataset does not have any source files.");
            }
            return sourceFiles[0];
        }
    }
}
