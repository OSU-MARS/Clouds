using System;

namespace Mars.Clouds.Extensions
{
    internal static class Int64Extensions
    {
        public static Int32 GetLowerInt32(this Int64 value)
        {
            return (Int32)(value & 0x0000000ffffffff);
        }

        public static Int32 GetUpperInt32(this Int64 value)
        {
            return (Int32)(value >> 32);
        }

        public static Int64 Pack(Int32 upper, Int32 lower)
        {
            return ((Int64)upper << 32) | unchecked((Int64)lower & 0x0000000ffffffff);
        }
    }
}
