using System;
using System.IO;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// Header of a version 1.3 .las file.
    /// </summary>
    public class LasHeader13 : LasHeader12
    {
        public new const int HeaderSizeInBytes = 235;

        public UInt64 StartOfWaveformDataPacketRecord { get; set; }

        public LasHeader13() 
        {
            this.VersionMinor = 3;
            this.HeaderSize = LasHeader13.HeaderSizeInBytes;
        }

        public override void Validate()
        {
            base.Validate();

            if ((this.StartOfWaveformDataPacketRecord != 0) && (this.StartOfWaveformDataPacketRecord <= this.OffsetToPointData))
            {
                throw new InvalidDataException("StartOfWaveformDataPacketRecord");
            }
        }
    }
}
