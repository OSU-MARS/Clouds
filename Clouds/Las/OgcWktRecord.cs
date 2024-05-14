using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Mars.Clouds.Las
{
    public class OgcWktRecord : VariableLengthRecord
    {
        public SpatialReference SpatialReference { get; protected set; }

        protected OgcWktRecord(UInt16 reserved, UInt16 recordID, UInt16 recordLengthAfterHeader, string description, ReadOnlySpan<byte> wktBytes)
            : this(reserved, recordID, recordLengthAfterHeader, description, new SpatialReference(Encoding.UTF8.GetString(wktBytes)))
        {
        }

        protected OgcWktRecord(UInt16 reserved, UInt16 recordID, UInt16 recordLengthAfterHeader, string description, SpatialReference crs)
            : base(reserved, LasFile.LasfProjection, recordID, recordLengthAfterHeader, description)
        {
            this.SpatialReference = crs;
        }

        public void SetSpatialReference(SpatialReference crs)
        {
            string wkt = crs.GetWkt();
            this.RecordLengthAfterHeader = (UInt16)Encoding.UTF8.GetBytes(wkt).Length;
            this.SpatialReference = crs;
        }

        public override void Write(Stream stream)
        {
            this.WriteHeader(stream);

            string wkt = this.SpatialReference.GetWkt();
            byte[] wktBytes = Encoding.UTF8.GetBytes(wkt);
            Debug.Assert(this.RecordLengthAfterHeader == wktBytes.Length);
            stream.Write(wktBytes);
        }
    }
}
