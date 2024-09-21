using OSGeo.OGR;
using System.IO;

namespace Mars.Clouds.GdalExtensions
{
    internal static class OgrExtensions
    {
        public static DataSource CreateOrOpenForWrite(string filePath)
        {
            if (File.Exists(filePath))
            {
                return Ogr.Open(filePath, update: 1);
            }
            
            return Ogr.GetDriverByName("GPKG").CreateDataSource(filePath, options: []);
        }

        public static DataSource OpenForRead(string filePath)
        {
            DataSource? dataSource = Ogr.Open(filePath, update: 0);
            if (dataSource == null)
            {
                throw new FileLoadException("GDAL returned a null DataSource. Does the file exist and is it well formed in a format supported by GDAL?", filePath);
            }

            return dataSource;
        }
    }
}
