using System;

namespace Mars.Clouds
{
    public static class Constant
    {
        public static readonly TimeSpan DefaultProgressInterval = TimeSpan.FromSeconds(1.5);

        public static class Epsg
        {
            public const int Navd88m = 5703; // metric
            public const int Utm10N = 32610;
            public const int Wgs84 = 4326;
        }

        public static class File
        {
            public const int DefaultBufferSize = 128 * 1024; // 128k
            public const string GeoPackageExtension = ".gpkg";
            public const string GeoTiffExtension = ".tif";
            public const string LasExtension = ".las";
            public const string XlsxExtension = ".xlsx";
            public const string XmlExtension = ".xml";
        }

        public static class Las
        {
            public const int HeaderAndVlrReadBufferSizeInBytes = 4 * 1024; // use 4k as compromise: header bytes + GeoTIFF CRS VLRs are ~512 bytes but wkt CRS VLRs are 5+ kB
            public const UInt16 VlrReservedHeaderValue = 0;
        }

        public static class Simd128
        {
            //public const byte BlendA0B1 = 0b000000010; // blendpd
            public const byte BlendA0B123 = 0b00001110; // blendps, vpblendd
            public const int Circular32Up1 = (0x3 << 0) | (0x0 << 2) | (0x1 << 4) | (0x2 << 6); // pshufd, shufps
            public const int Circular32Up2 = (0x2 << 0) | (0x3 << 2) | (0x0 << 4) | (0x1 << 6); // pshufd, shufps
            public const int Circular32Up3 = (0x1 << 6) | (0x2 << 0) | (0x3 << 2) | (0x0 << 4); // pshufd, shufps
            //public const int Circular64Up1 = (0x1 << 0) | (0x0 << 1); // shufpd
            public const byte ExtractUpper64 = 0b00000001; // pextrq
        }
    }
}
