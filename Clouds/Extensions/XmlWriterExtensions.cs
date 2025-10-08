using Mars.Clouds.Vrt;
using OSGeo.GDAL;
using System;
using System.Globalization;
using System.Text;
using System.Xml;

namespace Mars.Clouds.Extensions
{
    internal static class XmlWriterExtensions
    {
        public static void WriteAttribute(this XmlWriter writer, string localName, DataType value)
        {
            writer.WriteAttributeString(localName, value.ToString()[4..]); // remove GDT_ prefix
        }

        public static void WriteAttribute(this XmlWriter writer, string localName, int value)
        {
            writer.WriteAttributeString(localName, value.ToString(CultureInfo.InvariantCulture));
        }

        public static void WriteAttributeBinary(this XmlWriter writer, string localName, bool value)
        {
            writer.WriteAttributeString(localName, value ? "1" : "0");
        }

        public static void WriteAttributeCsv(this XmlWriter writer, string localName, int[] values)
        {
            StringBuilder valuesAsString = new();
            for (int valueIndex = 0; valueIndex < values.Length - 1; ++valueIndex)
            {
                valuesAsString.Append(values[valueIndex].ToString(CultureInfo.InvariantCulture));
                valuesAsString.Append(',');
            }
            if (values.Length > 0)
            {
                valuesAsString.Append(values[^1].ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteAttributeString(localName, valuesAsString.ToString());
        }

        public static void WriteElementString(this XmlWriter writer, string localName, ColorInterpretation value)
        {
            writer.WriteElementString(localName, value.ToString());
        }

        public static void WriteElementString(this XmlWriter writer, string localName, double value)
        {
            writer.WriteElementString(localName, value.ToString(CultureInfo.InvariantCulture));
        }

        public static void WriteElementString(this XmlWriter writer, string localName, int value)
        {
            writer.WriteElementString(localName, value.ToString(CultureInfo.InvariantCulture));
        }

        public static void WriteString(this XmlWriter writer, object value)
        {
            if (value is bool booleanValue)
            {
                writer.WriteString(booleanValue.ToString(CultureInfo.InvariantCulture));
            }
            else if (value is double doubleValue) 
            {
                writer.WriteString(doubleValue.ToString(CultureInfo.InvariantCulture));
            }
            else if (value is string stringValue)
            {
                writer.WriteString(stringValue);
            }
            else
            {
                throw new NotSupportedException($"Unhandled value '{value}' of type {value.GetType().FullName}.");
            }
        }
    }
}