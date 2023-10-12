using System;
using System.IO;

namespace Mars.Clouds.Las
{
    public class LasHeader12 : LasHeader11
    {
        /// <summary>
        /// 
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
            if (this.GlobalEncoding > (GlobalEncoding.GpsTimeType | GlobalEncoding.WaveformDataPacketsExternal | GlobalEncoding.SyntheticReturnNumbers | GlobalEncoding.WellKnownText))
            {
                throw new InvalidDataException("GlobalEncoding");
            }
        }
    }
}
