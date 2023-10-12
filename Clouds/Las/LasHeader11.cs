using System;

namespace Mars.Clouds.Las
{
    public class LasHeader11 : LasHeader10
    {
        /// <summary>
        /// Unique ID of source of .las file. Zero if no ID has been assigned (default).
        /// </summary>
        /// <remarks>
        /// Required.
        /// </remarks>
        public UInt16 FileSourceID { get; set; }

        public LasHeader11()
        {
            this.VersionMinor = 1;
        }

        // nothing to validate in FileSourceID
        // public override Validate()
    }
}
