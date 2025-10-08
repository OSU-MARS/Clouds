using Mars.Clouds.Vrt;
using OSGeo.GDAL;
using System;
using System.Globalization;
using System.Xml;

namespace Mars.Clouds.Extensions
{
    internal static class XmlReaderExtensions
    {
        public static bool ReadAttributeAsBoolean(this XmlReader reader, string attributeName) 
        {
            string attributeValueAsString = reader.ReadAttributeAsString(attributeName);
            if (Boolean.TryParse(attributeValueAsString, out bool attributeValue) == false)
            {
                return attributeValueAsString switch
                {
                    "1" => true,
                    "0" => false,
                    _ => throw new XmlException($"{reader.Name}@{attributeName} has non-boolean value '{attributeValueAsString}'.")
                };
            }

            return attributeValue;
        }

        public static int[] ReadAttributeAsCsvIntegerArray(this XmlReader reader, string attributeName)
        {
            string[] attributeValueAsStringArray = reader.ReadAttributeAsString(attributeName).Split(',');
            int[] attributeValue = new int[attributeValueAsStringArray.Length];
            for (int index = 0; index < attributeValueAsStringArray.Length; ++index)
            {
                if (Int32.TryParse(attributeValueAsStringArray[index], out int value) == false)
                {
                    throw new XmlException($"{reader.Name} contains non-integer point value '{attributeValueAsStringArray[index]}'.");
                }

                attributeValue[index] = value;
            }

            return attributeValue;
        }

        public static double ReadAttributeAsDouble(this XmlReader reader, string attributeName)
        {
            string attributeValueAsString = reader.ReadAttributeAsString(attributeName);
            if (Double.TryParse(attributeValueAsString, out double attributeValue) == false)
            {
                throw new XmlException($"{reader.Name}@{attributeName} has non-floating point value '{attributeValueAsString}'.");
            }

            return attributeValue;
        }

        public static float ReadAttributeAsFloat(this XmlReader reader, string attributeName)
        {
            string attributeValueAsString = reader.ReadAttributeAsString(attributeName);
            if (Single.TryParse(attributeValueAsString, out float attributeValue) == false)
            {
                throw new XmlException($"{reader.Name}@{attributeName} has non-floating point value '{attributeValueAsString}'.");
            }

            return attributeValue;
        }

        public static DataType ReadAttributeAsGdalDataType(this XmlReader reader, string attributeName)
        {
            string attributeValueAsString = reader.ReadAttributeAsString(attributeName);
            return attributeValueAsString switch
            {
                "Byte" => DataType.GDT_Byte,
                "Float32" => DataType.GDT_Float32,
                "Float64" => DataType.GDT_Float64,
                "Int8" => DataType.GDT_Int8,
                "Int16" => DataType.GDT_Int16,
                "Int32" => DataType.GDT_Int32,
                "Int64" => DataType.GDT_Int64,
                "UInt16" => DataType.GDT_UInt16,
                "UInt32" => DataType.GDT_UInt32,
                "UInt64" => DataType.GDT_UInt64,
                _ => throw new NotSupportedException($"Unhandled GDAL data type '{attributeValueAsString}'.")
            };
        }

        public static Int32 ReadAttributeAsInt32(this XmlReader reader, string attributeName)
        {
            string attributeValueAsString = reader.ReadAttributeAsString(attributeName);
            if (Int32.TryParse(attributeValueAsString, out Int32 attributeValue) == false) 
            {
                throw new XmlException($"{reader.Name}@{attributeName} has non-integer value '{attributeValueAsString}'.");
            }

            return attributeValue;
        }

        public static string ReadAttributeAsString(this XmlReader reader, string attributeName)
        {
            string? attributeValueAsString = reader.GetAttribute(attributeName);
            if (attributeValueAsString == null)
            {
                throw new XmlException($"{reader.Name}@{attributeName} is not present.");
            }

            return attributeValueAsString;
        }

        public static UInt32 ReadAttributeAsUInt32(this XmlReader reader, string attributeName)
        {
            string attributeValueAsString = reader.ReadAttributeAsString(attributeName);
            if (UInt32.TryParse(attributeValueAsString, out UInt32 attributeValue) == false)
            {
                throw new XmlException($"{reader.Name}@{attributeName} has non-integer value '{attributeValueAsString}'.");
            }

            return attributeValue;
        }

        public static UInt32 ReadAttributeAsUInt32Hex(this XmlReader reader, string attributeName)
        {
            string attributeValueAsString = reader.ReadAttributeAsString(attributeName); 
            if (attributeValueAsString.StartsWith("0x") == false)
            {
                throw new XmlException($"{reader.Name}@{attributeName} does not start with '0x'.");
            }
            if (UInt32.TryParse(attributeValueAsString[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out UInt32 attributeValue) == false)
            {
                throw new XmlException($"{reader.Name}@{attributeName} has non-hexadecimal value '{attributeValueAsString}'.");
            }
            return attributeValue;
        }
        public static VrtDatasetSubclass ReadAttributeAsVrtDatasetSubclass(this XmlReader reader, string attributeName)
        {
            string attributeValueAsString = reader.ReadAttributeAsString(attributeName);
            return attributeValueAsString switch
            {
                "VRTPansharpenedDataset" => VrtDatasetSubclass.VrtPansharpenedDataset,
                "VRTProcessedDataset" => VrtDatasetSubclass.VrtProcessedDataset,
                "VRTWarpedDataset" => VrtDatasetSubclass.VrtWarpedDataset,
                _ => throw new NotSupportedException($"Unhandled VRT dataset subclass '{attributeValueAsString}'.")
            };
        }


        public static double[] ReadElementContentAsCsvDoubleArray(this XmlReader reader)
        {
            string[] elementAsStringArray = reader.ReadElementContentAsString().Split(',');
            double[] elementValue = new double[elementAsStringArray.Length];
            for (int index = 0; index < elementAsStringArray.Length; ++index)
            {
                if (Double.TryParse(elementAsStringArray[index], out double value) == false)
                {
                    throw new XmlException($"{reader.Name} contains non-floating point value '{elementAsStringArray[index]}'.");
                }

                elementValue[index] = value;
            }

            return elementValue;
        }

        public static TEnum ReadElementContentAsEnum<TEnum>(this XmlReader reader) where TEnum : struct 
        {
            string attributeValueAsString = reader.ReadElementContentAsString();
            if (Enum.TryParse<TEnum>(attributeValueAsString, out TEnum elementValue) == false)
            {
                throw new XmlException($"{reader.Name} contains unknown {typeof(Enum).Name} value '{attributeValueAsString}'.");
            }

            return elementValue;
        }

        public static UInt32[] ReadElementContentAsPipeUIntArray(this XmlReader reader)
        {
            string[] elementAsStringArray = reader.ReadElementContentAsString().Split('|');
            UInt32[] elementValue = new UInt32[elementAsStringArray.Length];
            for (int index = 0; index < elementAsStringArray.Length; ++index)
            {
                if (UInt32.TryParse(elementAsStringArray[index], out UInt32 value) == false)
                {
                    throw new XmlException($"{reader.Name} contains non-integer value '{elementAsStringArray[index]}'.");
                }

                elementValue[index] = value;
            }

            return elementValue;
        }

        public static UInt32 ReadElementContentAsUInt32(this XmlReader reader)
        {
            string attributeValueAsString = reader.ReadElementContentAsString();
            if (UInt32.TryParse(attributeValueAsString, out UInt32 elementValue) == false)
            {
                throw new XmlException($"{reader.Name} contains non-integer or signed integer value '{attributeValueAsString}'.");
            }

            return elementValue;
        }

    }
}
