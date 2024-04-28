using System;
using System.Globalization;
using System.Text;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    internal static class XmlWriterExtensionsVrt
    {
        public static void WriteAttributeDoubleOrNan(this XmlWriter writer, string localName, double value)
        {
            // .vrt schema requires nan or NAN, not NaN
            string valueAsString = Double.IsNaN(value) ? "nan" : value.ToString(CultureInfo.InvariantCulture);
            writer.WriteAttributeString(localName, valueAsString);
        }

        public static void WriteAttribute(this XmlWriter writer, string localName, VrtDatasetSubclass subclass)
        {
            string subclassAsString = subclass switch
            {
                VrtDatasetSubclass.None => throw new ArgumentOutOfRangeException(nameof(subclass), ""),
                VrtDatasetSubclass.VrtPansharpenedDataset => "VRTPansharpenedDataset",
                VrtDatasetSubclass.VrtProcessedDataset => "VRTProcessedDataset",
                VrtDatasetSubclass.VrtWarpedDataset => "VRTWarpedDataset",
                _ => throw new NotSupportedException("Unhandled VRT dataset subclass " + subclass + ".")
            };
            writer.WriteAttributeString(localName, subclassAsString);
        }

        public static void WriteElementBinary(this XmlWriter writer, string localName, bool value)
        {
            writer.WriteElementString(localName, value ? "1" : "0");
        }

        public static void WriteElementCsv(this XmlWriter writer, string localName, double[] values)
        {
            writer.WriteElementDelimited(localName, values, ',');
        }

        private static void WriteElementDelimited(this XmlWriter writer, string localName, double[] values, char delimiter)
        {
            StringBuilder valuesAsString = new();
            for (int valueIndex = 0; valueIndex < values.Length - 1; ++valueIndex)
            {
                valuesAsString.Append(values[valueIndex].ToString(CultureInfo.InvariantCulture));
                valuesAsString.Append(delimiter);
            }
            if (values.Length > 0)
            {
                valuesAsString.Append(values[^1].ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteElementString(localName, valuesAsString.ToString());
        }

        public static void WriteElementDoubleOrNaN(this XmlWriter writer, string localName, double value)
        {
            // .vrt schema requires nan or NAN, not NaN
            string valueAsString = Double.IsNaN(value) ? "nan" : value.ToString(CultureInfo.InvariantCulture);
            writer.WriteElementString(localName, valueAsString);
        }

        public static void WriteElementPipe(this XmlWriter writer, string localName, double[] values)
        {
            writer.WriteElementDelimited(localName, values, '|');
        }
    }
}
