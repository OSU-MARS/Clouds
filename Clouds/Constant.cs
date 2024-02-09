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
    }
}
