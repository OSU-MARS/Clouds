using System.IO;

namespace Mars.Clouds.Las
{
    public class LasHeader12 : LasHeader11
    {
        /// <summary>
        /// Only low bit (GPS time type) defined in LAS 1.2. LAS 1.3 adds waveform packets and synthetic returns, LAS 1.4 wkt.
        /// </summary>
        public GlobalEncoding GlobalEncoding { get; set; }

        public LasHeader12()
        {
            this.VersionMinor = 2;
        }

        public override void Validate()
        {
            base.Validate();

            // currently permissive: allows any known combination of flags with any LAS version
            // Can be made more restrictive if needed. Enum.IsDefined() doesn't support combinations of flags.
            if (this.GlobalEncoding > (GlobalEncoding.AdjustedStandardGpsTime | GlobalEncoding.WaveformDataPacketsExternal | GlobalEncoding.SyntheticReturnNumbers | GlobalEncoding.WellKnownText))
            {
                throw new InvalidDataException("GlobalEncoding");
            }
        }
    }
}
