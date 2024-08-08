using System;

namespace Mars.Clouds
{
    public static class Constant
    {
        public const int DefaultMaximumThreads = 256;
        public static readonly TimeSpan DefaultProgressInterval = TimeSpan.FromSeconds(1.5);

        public static class Epsg
        {
            public const int Navd88ft = 8228; // English units
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
            public const byte ReturnNumberMask10 = 0x07;
            public const byte ReturnNumberMask14 = 0x0f;
            public const UInt16 VlrReservedHeaderValue = 0;
        }

        public static class Simd128
        {
            //public const byte BlendA0B1 = 0b000000010; // blendpd
            public const byte BlendA0B123 = 0b00001110; // blendps, vpblendd
            public const int Circular32Up1 = (0x3 << 0) | (0x0 << 2) | (0x1 << 4) | (0x2 << 6); // pshufd, shufps
            public const int Circular32Up2 = (0x2 << 0) | (0x3 << 2) | (0x0 << 4) | (0x1 << 6); // pshufd, shufps
            //public const int Circular32Up3 = (0x1 << 0) | (0x2 << 2) | (0x3 << 4) | (0x0 << 6); // pshufd, shufps
            public const int Copy32OneToZero = (0x1 << 0) | (0x1 << 2) | (0x2 << 4) | (0x3 << 6); // vpermilps
            public const int Copy32TwoToZero = (0x2 << 0) | (0x1 << 2) | (0x2 << 4) | (0x3 << 6); // vpermilps
            public const int Copy32ThreeToZero = (0x3 << 0) | (0x1 << 2) | (0x2 << 4) | (0x3 << 6); // vpermilps
            //public const int Circular64Up1 = (0x1 << 0) | (0x0 << 1); // shufpd
            //public const byte ExtractUpper64 = 0x1; // pextrq
        }
    }
}
