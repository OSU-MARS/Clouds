using System;

namespace Mars.Clouds.Las
{
    public class GeoKeyEntry
    {
        public UInt16 KeyID { get; set; }
        public UInt16 TiffTagLocation { get; set; }
        public UInt16 Count { get; set; }
        public UInt16 ValueOrOffset { get; set; }
    }
}
