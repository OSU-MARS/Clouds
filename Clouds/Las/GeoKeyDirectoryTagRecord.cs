using System;
using System.Collections.Generic;

namespace Mars.Clouds.Las
{
    public class GeoKeyDirectoryTagRecord : VariableLengthRecordBase<UInt16>
    {
        public const UInt16 LasfProjectionRecordID = 34735;

        public UInt16 KeyDirectoryVersion { get; set; }
        public UInt16 KeyRevision { get; set; }
        public UInt16 MinorRevision { get; set; }
        public UInt16 NumberOfKeys { get; set; }
        public List<GeoKeyEntry> KeyEntries { get; set; }

        public GeoKeyDirectoryTagRecord() 
        {
            this.KeyEntries = new();
            this.RecordID = GeoKeyDirectoryTagRecord.LasfProjectionRecordID;
            this.UserID = LasFile.LasfProjection;
        }
    }
}
