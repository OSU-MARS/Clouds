using OSGeo.GDAL;

namespace Mars.Clouds.GdalExtensions
{
    public class RasterBandSlope : RasterBand<float>
    {
        public RasterBandSlope(Dataset rasterDataset, Band gdalBand, bool readData)
            : base(rasterDataset, gdalBand, readData)
        {
        }

        public RasterBandSlope(Raster raster, string name, float noDataValue, RasterBandInitialValue initialValue, RasterBandPool? dataBufferPool) 
            : base(raster, name, noDataValue, initialValue, dataBufferPool)
        {
        }

        public override RasterBandStatistics GetStatistics()
        {
            return new(this.Data, this.HasNoDataValue, this.NoDataValue, 0.0F, 90.0F, 1.0F);
        }
    }
}
