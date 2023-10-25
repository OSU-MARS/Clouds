using System;

namespace Mars.Clouds.Laz
{
    [Flags]
    public enum LazOptions : UInt32
    {
        None = 0x0,

        /// <summary>
        /// Backwards compatibility mode where LAS 1.4 points are represented using LAS 1.3 or earlier point types.
        /// </summary>
        CompatibilityMode = 0x1
    }
}
