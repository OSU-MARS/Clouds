using Mars.Clouds.Extensions;
using System;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class IopsBucket : XmlSerializable
    {
        public int SampleMillisecond { get; private set; }
        public int Read { get; private set; }
        public int Write { get; private set; }
        public int Total { get; private set; }
        public float ReadMinLatencyMilliseconds { get; private set; }
        public float ReadMaxLatencyMilliseconds { get; private set; }
        public float ReadAvgLatencyMilliseconds { get; private set; }
        public float ReadLatencyStdDev { get; private set; }
        public float WriteMinLatencyMilliseconds { get; private set; }
        public float WriteMaxLatencyMilliseconds { get; private set; }
        public float WriteAvgLatencyMilliseconds { get; private set; }
        public float WriteLatencyStdDev { get; private set; }
        
        public IopsBucket()
        {
            this.SampleMillisecond = -1;
            this.Read = -1;
            this.Write = -1;
            this.Total = -1;
            this.ReadMinLatencyMilliseconds = Single.NaN;
            this.ReadMaxLatencyMilliseconds = Single.NaN;
            this.ReadAvgLatencyMilliseconds = Single.NaN;
            this.ReadLatencyStdDev = Single.NaN;
            this.WriteMinLatencyMilliseconds = Single.NaN;
            this.WriteMaxLatencyMilliseconds = Single.NaN;
            this.WriteAvgLatencyMilliseconds = Single.NaN;
            this.WriteLatencyStdDev = Single.NaN;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 12)
            {
                throw new XmlException($"Encountered unexpected attributes on element '{reader.Name}'.");
            }

            switch (reader.Name)
            {
                case "Bucket":
                    this.SampleMillisecond = reader.ReadAttributeAsInt32("SampleMillisecond");
                    this.Read = reader.ReadAttributeAsInt32("Read");
                    this.Write = reader.ReadAttributeAsInt32("Write");
                    this.Total = reader.ReadAttributeAsInt32("Total");
                    this.ReadMinLatencyMilliseconds = reader.ReadAttributeAsFloat("ReadMinLatencyMilliseconds");
                    this.ReadMaxLatencyMilliseconds = reader.ReadAttributeAsFloat("ReadMaxLatencyMilliseconds");
                    this.ReadAvgLatencyMilliseconds = reader.ReadAttributeAsFloat("ReadAvgLatencyMilliseconds");
                    this.ReadLatencyStdDev = reader.ReadAttributeAsFloat("ReadLatencyStdDev");
                    this.WriteMinLatencyMilliseconds = reader.ReadAttributeAsFloat("WriteMinLatencyMilliseconds");
                    this.WriteMaxLatencyMilliseconds = reader.ReadAttributeAsFloat("WriteMaxLatencyMilliseconds");
                    this.WriteAvgLatencyMilliseconds = reader.ReadAttributeAsFloat("WriteAvgLatencyMilliseconds");
                    this.WriteLatencyStdDev = reader.ReadAttributeAsFloat("WriteLatencyStdDev");
                    reader.Read();
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
