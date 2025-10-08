using OSGeo.GDAL;
using System;
using System.IO;

namespace Mars.Clouds.GdalExtensions
{
    internal static class GdalExtensions
    {
        public static Driver GetDriverByExtension(string filePath)
        {
            string fileExtension = Path.GetExtension(filePath).ToLowerInvariant();
            string driverName = fileExtension switch
            {
                Constant.File.GeoTiffExtension or ".tiff" => "GTiff",
                _ => throw new NotSupportedException($"Unknown file extension '{fileExtension}' in path '{filePath}'.")
            };

            Driver? driver = Gdal.GetDriverByName(driverName); // caller is responsible for disposal
            if (driver == null)
            {
                throw new InvalidOperationException($"GDAL returned null for driver '{driverName}'.");
            }

            return driver;
        }
    }
}
