using Mars.Clouds.GdalExtensions;
using MaxRev.Gdal.Core;
using OSGeo.GDAL;
using System;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    public class GdalCmdlet : Cmdlet
    {
        static GdalCmdlet()
        {
            GdalBase.ConfigureAll();
        }

        protected static Raster<float> ReadRasterFloat(string rasterPath)
        {
            using Dataset rasterDataset = Gdal.Open(rasterPath, Access.GA_ReadOnly);
            (DataType rasterDataType, long totalCells) = Raster.GetBandProperties(rasterDataset);
            if (rasterDataType != DataType.GDT_Float32)
            {
                throw new NotSupportedException("Raster '" + rasterPath + "' has data type " + rasterDataType + ".");
            }
            if (totalCells > Array.MaxLength)
            {
                throw new NotSupportedException("Raster '" + rasterPath + "' has " + totalCells + " cells, which exceeds the maximum supported size of " + Array.MaxLength + " cells.");
            }

            return new Raster<float>(rasterDataset)
            {
                FilePath = rasterPath
            };
        }
    }
}
