using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using System;
using System.Collections.Generic;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    public class VrtMetadata : XmlSerializable
    {
        // not implemented: @domain, @format

        // well known metadata keys
        // See also the GDAL raster data model, https://gdal.org/user/raster_data_model.html.
        public const string OgrBooleanYes = "YES";
        public const string OgrBooleanNo = "NO";
        public const string StatisticsApproximate = "STATISTICS_APPROXIMATE";
        public const string StatisticsMaximum = "STATISTICS_MAXIMUM";
        public const string StatisticsMean = "STATISTICS_MEAN";
        public const string StatisticsMinimum = "STATISTICS_MINIMUM";
        public const string StatisticsStdDev = "STATISTICS_STDDEV";
        public const string StatisticsValidPercent = "STATISTICS_VALID_PERCENT";

        private readonly Dictionary<string, object> metadataItems;

        public VrtMetadata()
        {
            this.metadataItems = [];
        }

        public object this[string key]
        {
            get { return this.metadataItems[key]; }
            set { this.metadataItems[key] = value; }
        }

        public int Count
        {
            get { return this.metadataItems.Count; }
        }

        public void Add(RasterBandStatistics statistics)
        {
            double dataFraction = statistics.GetDataFraction();
            string approximate = statistics.IsApproximate ? VrtMetadata.OgrBooleanYes : VrtMetadata.OgrBooleanNo; 
            this.metadataItems.Add(VrtMetadata.StatisticsApproximate, approximate);
            this.metadataItems.Add(VrtMetadata.StatisticsMaximum, statistics.Maximum);
            this.metadataItems.Add(VrtMetadata.StatisticsMean, statistics.Mean);
            this.metadataItems.Add(VrtMetadata.StatisticsMinimum, statistics.Minimum);
            this.metadataItems.Add(VrtMetadata.StatisticsStdDev, statistics.StandardDeviation);
            this.metadataItems.Add(VrtMetadata.StatisticsValidPercent, 100.0 * dataFraction);
        }

        private static object ParseMetadataValue(string key, string value)
        {
            return key switch
            {
                VrtMetadata.StatisticsApproximate => VrtMetadata.ParseOgrBoolean(value),
                VrtMetadata.StatisticsMaximum => Double.Parse(value),
                VrtMetadata.StatisticsMean => Double.Parse(value),
                VrtMetadata.StatisticsMinimum => Double.Parse(value),
                VrtMetadata.StatisticsStdDev => Double.Parse(value),
                VrtMetadata.StatisticsValidPercent => Double.Parse(value),
                _ => throw new NotSupportedException("Unhandled .vrt raster band metadata key '" + key + "'.")
            };
        }

        private static bool ParseOgrBoolean(string valueAsString)
        {
            if (Boolean.TryParse(valueAsString, out bool value) == false)
            {
                if (String.Equals(valueAsString, "yes", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(valueAsString, "1", StringComparison.Ordinal) ||
                    String.Equals(valueAsString, "on", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (String.Equals(valueAsString, "no", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(valueAsString, "0", StringComparison.Ordinal) ||
                    String.Equals(valueAsString, "off", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                throw new NotSupportedException("'" + valueAsString + "' is not a well known boolean value.");
            }

            return value;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            switch (reader.Name)
            {
                case "Metadata":
                    if (reader.AttributeCount != 0)
                    {
                        throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
                    }
                    reader.Read();
                    break;
                case "MDI":
                    if (reader.AttributeCount != 1)
                    {
                        throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
                    }
                    string key = reader.ReadAttributeAsString("key");
                    string value = reader.ReadElementContentAsString();
                    this.metadataItems.Add(key, VrtMetadata.ParseMetadataValue(key, value));
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }

        public void WriteXml(XmlWriter writer) 
        {
            if (this.metadataItems.Count < 1)
            {
                return; // nothing to do
            }

            writer.WriteStartElement("Metadata");
            foreach (KeyValuePair<string, object> metadataItem in this.metadataItems)
            {
                writer.WriteStartElement("MDI");
                writer.WriteAttributeString("key", metadataItem.Key);
                writer.WriteString(metadataItem.Value);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
    }
}
