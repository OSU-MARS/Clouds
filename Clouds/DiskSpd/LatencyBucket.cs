using System;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class LatencyBucket : XmlSerializable
    {
        public float Percentile { get; private set; }
        public float ReadMilliseconds { get; private set; }
        public float WriteMilliseconds { get; private set; }
        public float TotalMilliseconds { get; private set; }

        public LatencyBucket()
        {
            this.Percentile = -1;
            this.ReadMilliseconds = Single.NaN;
            this.WriteMilliseconds = Single.NaN;
            this.TotalMilliseconds = Single.NaN;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "Bucket":
                    reader.Read();
                    break;
                case "Percentile":
                    this.Percentile = reader.ReadElementContentAsFloat();
                    break;
                case "ReadMilliseconds":
                    this.ReadMilliseconds = reader.ReadElementContentAsFloat();
                    break;
                case "WriteMilliseconds":
                    this.WriteMilliseconds = reader.ReadElementContentAsFloat();
                    break;
                case "TotalMilliseconds":
                    this.TotalMilliseconds = reader.ReadElementContentAsFloat();
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
