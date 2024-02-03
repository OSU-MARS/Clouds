using Mars.Clouds.Las;
using System;

namespace Mars.Clouds.GdalExtensions
{
    public static class RasterExtensions
    {
        // implemented as extension to provide concreate source type
        // Can be collapsed to generic math given satisfactory profiling results.
        public static ImageRaster<Int16> AsInt16(this Raster<UInt64> source, Int16 noDataValue)
        {
            // TODO: deep copy CRS and transform?
            ImageRaster<Int16> int16raster = new(source.Crs, source.Transform, source.XSize, source.YSize, noDataValue);
            for (int bandIndex = 0; bandIndex < source.BandCount; ++bandIndex)
            {
                RasterBand<UInt64> sourceBand = source.Bands[bandIndex];

                RasterBand<Int16> destinationBand = int16raster.Bands[bandIndex];
                destinationBand.Name = sourceBand.Name;
                destinationBand.SetNoDataValue(noDataValue);

                // multi-instruction SIMD packing needed
                // need AVX-512 for _mm_cvtepi64_epi16, no _mm_cvtepu64_epi16
                for (int cellIndex = 0; cellIndex < source.CellsPerBand; ++cellIndex)
                {
                    UInt64 sourceValue = sourceBand[cellIndex];
                    if (sourceBand.HasNoDataValue && sourceBand.IsNoData(sourceValue))
                    {
                        destinationBand[cellIndex] = noDataValue;
                    }
                    else
                    {
                        destinationBand[cellIndex] = (Int16)sourceValue;
                    }
                }
            }

            return int16raster;
        }

        // implemented as extension to provide concreate source type
        // Can be collapsed to generic math given satisfactory profiling results.
        public static ImageRaster<Int32> AsInt32(this Raster<UInt64> source, Int32 noDataValue)
        {
            // TODO: deep copy CRS and transform?
            ImageRaster<Int32> int32raster = new(source.Crs, source.Transform, source.XSize, source.YSize, noDataValue);
            for (int bandIndex = 0; bandIndex < source.BandCount; ++bandIndex)
            {
                RasterBand<UInt64> sourceBand = source.Bands[bandIndex];

                RasterBand<Int32> destinationBand = int32raster.Bands[bandIndex];
                destinationBand.Name = sourceBand.Name;
                destinationBand.SetNoDataValue(noDataValue);

                // multi-instruction SIMD packing needed
                // need AVX-512 for _mm_cvtepi64_epi16, no _mm_cvtepu64_epi16
                for (int cellIndex = 0; cellIndex < source.CellsPerBand; ++cellIndex)
                {
                    UInt64 sourceValue = sourceBand[cellIndex];
                    if (sourceBand.HasNoDataValue && sourceBand.IsNoData(sourceValue))
                    {
                        destinationBand[cellIndex] = noDataValue;
                    }
                    else
                    {
                        destinationBand[cellIndex] = (Int32)sourceValue;
                    }
                }
            }

            return int32raster;
        }
    }
}
