using OSGeo.OSR;
using System;

namespace Mars.Clouds.Las
{
    public class OgcCoordinateSystemWktRecord : VariableLengthRecordBase<UInt16>
    {
        public const UInt16 LasfProjectionRecordID = 2112;

        public SpatialReference SpatialReference { get; set; }

        public OgcCoordinateSystemWktRecord(SpatialReference spatialReference)
        {
            this.RecordID = OgcCoordinateSystemWktRecord.LasfProjectionRecordID;
            this.SpatialReference = spatialReference;
            this.UserID = LasFile.LasfProjection;
        }
    }
}
