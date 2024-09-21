using OSGeo.GDAL;

namespace Mars.Clouds.GdalExtensions
{
    public class RasterBandAspect : RasterBand<float>
    {
        public RasterBandAspect(Dataset rasterDataset, Band gdalBand, bool readData)
            : base(rasterDataset, gdalBand, readData)
        {
        }

        public RasterBandAspect(Raster raster, string name, float noDataValue, RasterBandInitialValue initialValue, RasterBandPool? dataBufferPool)
            : base(raster, name, noDataValue, initialValue, dataBufferPool)
        {
        }

        public override RasterBandStatistics GetStatistics()
        {
            return new(this.Data, this.HasNoDataValue, this.NoDataValue, 0.0F, 360.0F, 1.0F);
        }
    }
}
