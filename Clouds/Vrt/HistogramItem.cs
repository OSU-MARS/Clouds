using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using System;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    public class HistogramItem : XmlSerializable
    {
        public bool Approximate { get; private set; }
        public int BucketCount { get; private set; }
        public UInt32[] BucketCounts { get; private set; }
        public double Maximum { get; private set; }
        public double Minimum { get; private set; }
        public bool IncludeOutOfRange { get; private set; }

        public HistogramItem()
        {
            this.Approximate = false;
            this.BucketCount = 0;
            this.BucketCounts = [];
            this.Maximum = Double.NaN;
            this.Minimum = Double.NaN;
            this.IncludeOutOfRange = false;
        }

        public HistogramItem(RasterBandStatistics statistics)
        {
            this.Approximate = statistics.IsApproximate;
            if (statistics.HasHistogram == false)
            {
                // band statistics lack histogram data: only a single bin histogram can be created
                this.BucketCount = 1;
                this.BucketCounts = [ (UInt32)Int64.Min(Int32.MaxValue, statistics.CellsSampled - statistics.NoDataCells) ];
                this.Maximum = statistics.Maximum;
                this.Minimum = statistics.Minimum;
                this.IncludeOutOfRange = false;
            }
            else
            {
                this.BucketCount = statistics.Histogram.Length;
                this.BucketCounts = new UInt32[statistics.Histogram.Length];
                Array.Copy(statistics.Histogram, this.BucketCounts, statistics.Histogram.Length);
                this.Maximum = statistics.HistogramMaximum;
                this.Minimum = statistics.HistogramMinimum;
                this.IncludeOutOfRange = statistics.HistogramIncludesOutOfRange;
            }
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException($"Encountered unexpected attributes on element '{reader.Name}'.");
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
                    this.BucketCount = reader.ReadElementContentAsInt();
                    break;
                case "IncludeOutOfRange":
                    this.IncludeOutOfRange = reader.ReadElementContentAsBoolean();
                    break;
                case "Approximate":
                    this.Approximate = reader.ReadElementContentAsBoolean();
                    break;
                case "HistCounts":
                    this.BucketCounts = reader.ReadElementContentAsPipeUIntArray();
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }

        public void WriteXml(XmlWriter writer)
        {
            if (this.BucketCount != this.BucketCounts.Length)
            {
                throw new InvalidOperationException($"Histogram is set for {this.BucketCount} buckets but counts are defined for {this.BucketCounts.Length} buckets.");
            }

            writer.WriteStartElement("HistItem");
            writer.WriteElementString("HistMin", this.Minimum);
            writer.WriteElementString("HistMax", this.Maximum);
            writer.WriteElementString("BucketCount", this.BucketCount);
            writer.WriteElementBinary("IncludeOutOfRange", this.IncludeOutOfRange);
            writer.WriteElementBinary("Approximate", this.Approximate);
            if (this.BucketCounts.Length > 0)
            {
                writer.WriteElementPipe("HistCounts", this.BucketCounts);
            }

            writer.WriteEndElement();
        }
    }
}