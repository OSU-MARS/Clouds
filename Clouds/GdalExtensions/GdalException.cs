using OSGeo.GDAL;
using System;

namespace Mars.Clouds.GdalExtensions
{
    public class GdalException(string? message) : Exception(message)
    {
        public static void ThrowIfError(CPLErr gdalErrorCode, string functionName)
        {
            if (gdalErrorCode != CPLErr.CE_None)
            {
                throw new GdalException(functionName + " returned " + gdalErrorCode + ".");
            }
        }
    }
}
