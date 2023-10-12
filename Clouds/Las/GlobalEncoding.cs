using System;

namespace Mars.Clouds.Las
{
    [Flags]
    public enum GlobalEncoding : UInt16
    {
        /// <summary>
        /// No flags set, encoding is as in LAS 1.0 and 1.1 (which lack a global encoding field).
        /// </summary>
        None = 0x0000,

        /// <summary>
        /// LAS 1.2+.
        /// Unset: GPS time is GPS week time (default in LAS 1.0 and 1.1, which lack a global encoding field).
        /// Set: GPS time is adjusted standard GPS time.
        /// </summary>
        GpsTimeType = 0x0001,

        /// <summary>
        /// Waveform data packets contained in this .las or .laz file. LAS 1.3+, Mutually exclusive with <see cref="WaveformDataPacketsExternal"/>.
        /// </summary>
        [Obsolete("Use " + nameof(WaveformDataPacketsExternal) + ".")]
        WaveformDataPacketsInternal = 0x0002,

        /// <summary>
        /// Waveform data packets are contained in an accompanying .wdp file. LAS 1.3+ Mutually exclusive with <see cref="WaveformDataPacketsExternal"/>
        /// </summary>
        WaveformDataPacketsExternal = 0x0004,

        /// <summary>
        /// Return numbers of points have been synthetically generated. LAS 1.3+
        /// </summary>
        SyntheticReturnNumbers = 0x0008,

        /// <summary>
        /// LAS 1.4+.
        /// Unset (backwards compatible): coordinate reference system is GeoTIFF.
        /// Set: coordinate reference system is WKT.
        /// </summary>
        WellKnownText = 0x0010

        // bits 5-15 reserved
    }
}
