using System;

namespace Mars.Clouds.Laz
{
    // from LASzip\src\laszip.hpp
    public enum LazItemType : UInt16
    {
        Byte,
        Short, 
        Int, 
        Long, 
        Float, 
        Double, 
        Point10, 
        Gpstime11, 
        Rgb12, 
        Wavepacket13, 
        Point14, 
        Rgb14, 
        RgbNir14,
        Wavepacket14, 
        Byte14
    }
}
