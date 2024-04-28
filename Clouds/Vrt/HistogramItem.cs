using Mars.Clouds.Extensions;
using System;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    public class HistogramItem : XmlSerializable
    {
        public bool Approximate { get; private set; }
        public int BucketCount { get; private set; }
        public int[] BucketCounts { get; private set; }
        public double Maximum { get; private set; }
        public double Minimum { get; private set; }
        public bool IncludeOutOfRange { get; private set; }

        public HistogramItem()
        {
            this.Approximate = false;
            this.BucketCount = -1;
            this.BucketCounts = [];
            this.Maximum = Double.NaN;
            this.Minimum = Double.NaN;
            this.IncludeOutOfRange = false;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "HistItem":
                    reader.Read();
                    break;
                case "HistMin":
                    this.Minimum = reader.ReadElementContentAsDouble();
                    break;
                case "HistMax":
                    this.Maximum = reader.ReadElementContentAsDouble();
                    break;
                case "BucketCount":
                    this.Maximum = reader.ReadElementContentAsInt();
                    break;
                case "IncludeOutOfRange":
                    this.IncludeOutOfRange = reader.ReadElementContentAsBoolean();
                    break;
                case "Approximate":
                    this.Approximate = reader.ReadElementContentAsBoolean();
                    break;
                case "HistCounts":
                    this.BucketCounts = reader.ReadElementContentAsPipeIntArray();
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteStartElement("HistItem");
            writer.WriteElementString("HistMin", this.Minimum);
            writer.WriteElementString("HistMax", this.Maximum);
            writer.WriteElementString("BucketCount", this.BucketCount);
            writer.WriteElementBinary("IncludeOutOfRange", this.IncludeOutOfRange);
            writer.WriteElementBinary("Approximate", this.Approximate);

            writer.WriteEndElement();
        }
    }
}