using OSGeo.OGR;
using System.IO;

namespace Mars.Clouds.GdalExtensions
{
    internal static class OgrExtensions
    {
        public static DataSource Open(string filePath)
        {
            if (File.Exists(filePath))
            {
                return Ogr.Open(filePath, update: 1);
            }
            
            return Ogr.GetDriverByName("GPKG").CreateDataSource(filePath, options: []);
        }
    }
}
