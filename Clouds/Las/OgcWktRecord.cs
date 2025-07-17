using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;
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

        public override int GetSizeInBytes()
        {
            string wkt = this.SpatialReference.GetWkt();
            byte[] wktBytes = Encoding.UTF8.GetBytes(wkt);
            return VariableLengthRecord.HeaderSizeInBytes + wktBytes.Length + 1; // wkt plus required trailing null
        }

        public void SetSpatialReference(SpatialReference crs)
        {
            string wkt = crs.GetWkt();
            this.RecordLengthAfterHeader = (UInt16)(Encoding.UTF8.GetBytes(wkt).Length + 1); // wkt plus required trailing null
            this.SpatialReference = crs;
        }

        public override void Write(Stream stream)
        {
            string wkt = this.SpatialReference.GetWkt();
            byte[] wktBytes = Encoding.UTF8.GetBytes(wkt);
            if (this.RecordLengthAfterHeader <= wktBytes.Length)
            {
                throw new InvalidOperationException("Serialized wkt (well known text) for coordinate system is of length " + wktBytes.Length + " which exceeds the " + this.RecordLengthAfterHeader + " byte record length after header after a trailing null is appended as required by the LAS 1.4 R15 specification.");
            }

            this.WriteHeader(stream);
            stream.Write(wktBytes);

            // only one trailing null is required but some .las file generators produce WKT records with extra bytes
            // Trimming extra bytes at serialization time is undesirable so, absent a better alternative, additional trailing nulls are written.
            for (int recordIndex = wktBytes.Length; recordIndex < this.RecordLengthAfterHeader; ++recordIndex)
            {
                stream.WriteByte(0);
            }
        }
    }
}
