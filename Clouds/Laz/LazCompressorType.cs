using System;

namespace Mars.Clouds.Laz
{
    // from LASzip\src\laszip.hpp
    public enum LazCompressorType : UInt16
    {
        None = 0,
        Pointwise = 1,
        PointwiseChunked = 2,
        LayeredChunked = 3
    }
}
