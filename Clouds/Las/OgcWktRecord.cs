using OSGeo.OSR;
using System;
using System.Text;

namespace Mars.Clouds.Las
{
    public class OgcWktRecord : VariableLengthRecordBase<UInt16>
    {
        public SpatialReference SpatialReference { get; set; }

        protected OgcWktRecord(UInt16 reserved, UInt16 recordID, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> wktBytes)
            : base(reserved, LasFile.LasfProjection, recordID, recordLengthAfterHeader, description)
        {
            this.SpatialReference = new(Encoding.UTF8.GetString(wktBytes));
        }
    }
}
