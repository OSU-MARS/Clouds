using System;
using System.Collections.Generic;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class Iops : XmlSerializable
    {
        public float ReadIopsStdDev { get; private set; }
        public float WriteIopsStdDev { get; private set; }
        public float IopsStdDev { get; private set; }
        public List<IopsBucket> Buckets { get; private set; }

        public Iops()
        {
            this.ReadIopsStdDev = Single.NaN;
            this.WriteIopsStdDev = Single.NaN;
            this.IopsStdDev = Single.NaN;
            this.Buckets = [];
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount == 0)
            {
                switch (reader.Name)
                {
                    case "Iops":
                        reader.Read();
                        break;
                    case "ReadIopsStdDev":
                        this.ReadIopsStdDev = reader.ReadElementContentAsFloat();
                        break;
                    case "WriteIopsStdDev":
                        this.WriteIopsStdDev = reader.ReadElementContentAsFloat();
                        break;
                    case "IopsStdDev":
                        this.IopsStdDev = reader.ReadElementContentAsFloat();
                        break;
                    default:
                        throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
                }
            }
            else
            {
                switch (reader.Name)
                {
                    case "Bucket":
                        IopsBucket bucket = new();
                        bucket.ReadXml(reader);
                        this.Buckets.Add(bucket);
                        break;
                    default:
                        throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
                }
            }
        }
    }
}
