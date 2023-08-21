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

        protected static SinglebandRaster<float> ReadSingleBandFloatRaster(string rasterPath)
        {
            using Dataset rasterDataset = Gdal.Open(rasterPath, Access.GA_ReadOnly);
            if (rasterDataset.RasterCount != 1)
            {
                throw new NotSupportedException("Raster '" + rasterPath + "' has " + rasterDataset.RasterCount + " bands.");
            }

            Band dsm = rasterDataset.GetRasterBand(1);
            if (dsm.DataType != DataType.GDT_Float32)
            {
                throw new NotSupportedException("Raster '" + rasterPath + "' band 1 has data type " + dsm.DataType + ".");
            }

            return new SinglebandRaster<float>(rasterDataset);
        }
    }
}
