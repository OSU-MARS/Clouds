using System;

namespace Mars.Clouds
{
    public static class Constant
    {
        public static readonly TimeSpan DefaultProgressInterval = TimeSpan.FromSeconds(2.0);

        public static class File
        {
            public const string GeoPackageExtension = ".gpkg";
            public const string GeoTiffExtension = ".tif";
            public const string LasExtension = ".las";
        }

        public static class Las
        {
            public const int HeaderAndVlrReadBufferSizeInBytes = 4 * 1024; // use 4k as compromise: header bytes + GeoTIFF CRS VLRs are ~512 bytes but wkt CRS VLRs are 5+ kB
        }

        public static class Simd128
        {
            public const byte BlendA0B123 = 0b00001110; // blendps, vpblendd
            public const byte CircularUp1 = (0x3 << 0) | (0x0 << 2) | (0x1 << 4) | (0x2 << 6); // pshufd, shufps
            public const byte ExtractUpper64 = 0b00000001; // pextrq
        }
    }
}
