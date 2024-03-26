using System;
using System.Collections.Generic;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class Latency : XmlSerializable
    {
        public float AverageReadMilliseconds { get; private set; }
        public float ReadLatencyStdev { get; private set; }
        public float AverageWriteMilliseconds { get; private set; }
        public float WriteLatencyStdev { get; private set; }
        public float AverageTotalMilliseconds { get; private set; }
        public float LatencyStdev { get; private set; }
        public List<LatencyBucket> Buckets { get; private set; }

        public Latency()
        {
            this.AverageReadMilliseconds = Single.NaN;
            this.ReadLatencyStdev = Single.NaN;
            this.AverageWriteMilliseconds = Single.NaN;
            this.WriteLatencyStdev = Single.NaN;
            this.AverageTotalMilliseconds = Single.NaN;
            this.LatencyStdev = Single.NaN;
            this.Buckets = [];
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "Latency":
                    reader.Read();
                    break;
                case "AverageReadMilliseconds":
                    this.AverageReadMilliseconds = reader.ReadElementContentAsFloat();
                    break;
                case "ReadLatencyStdev":
                    this.ReadLatencyStdev = reader.ReadElementContentAsFloat();
                    break;
                case "AverageWriteMilliseconds":
                    this.AverageWriteMilliseconds = reader.ReadElementContentAsFloat();
                    break;
                case "WriteLatencyStdev":
                    this.WriteLatencyStdev = reader.ReadElementContentAsFloat();
                    break;
                case "AverageTotalMilliseconds":
                    this.AverageTotalMilliseconds = reader.ReadElementContentAsFloat();
                    break;
                case "LatencyStdev":
                    this.LatencyStdev = reader.ReadElementContentAsFloat();
                    break;
                case "Bucket":
                    LatencyBucket bucket = new();
                    bucket.ReadXml(reader);
                    this.Buckets.Add(bucket);
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
