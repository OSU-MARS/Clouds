using Mars.Clouds.Extensions;
using OSGeo.GDAL;
using System;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    public class SourceProperties : XmlSerializable
    {
        public UInt32 BlockXSize { get; set; }
        public UInt32 BlockYSize { get; set; }
        public DataType DataType { get; set; }
        public UInt32 RasterXSize { get; set; }
        public UInt32 RasterYSize { get; set; }

        public SourceProperties()
        {
            this.BlockXSize = UInt32.MaxValue;
            this.BlockYSize = UInt32.MaxValue;
            this.DataType = DataType.GDT_Unknown;
            this.RasterXSize = UInt32.MaxValue;
            this.RasterYSize = UInt32.MaxValue;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            switch (reader.Name)
            {
                case "SourceProperties":
                    if ((reader.AttributeCount != 3) && (reader.AttributeCount != 5))
                    {
                        throw new XmlException($"Encountered unexpected attributes on element '{reader.Name}'.");
                    }
                    this.RasterXSize = reader.ReadAttributeAsUInt32("RasterXSize");
                    this.RasterYSize = reader.ReadAttributeAsUInt32("RasterYSize");
                    this.DataType = reader.ReadAttributeAsGdalDataType("DataType");
                    if (reader.AttributeCount == 5)
                    {
                        this.BlockXSize = reader.ReadAttributeAsUInt32("BlockXSize");
                        this.BlockYSize = reader.ReadAttributeAsUInt32("BlockYSize");
                    }
                    reader.Read();
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteStartElement("SourceProperties");
            writer.WriteAttributeDoubleOrNan("RasterXSize", this.RasterXSize);
            writer.WriteAttributeDoubleOrNan("RasterYSize", this.RasterYSize);
            writer.WriteAttribute("DataType", this.DataType);
            if (this.BlockXSize != UInt32.MaxValue)
            {
                writer.WriteAttributeDoubleOrNan("BlockXSize", this.BlockXSize);
            }
            if (this.BlockYSize != UInt32.MaxValue)
            {
                writer.WriteAttributeDoubleOrNan("BlockYSize", this.BlockYSize);
            }
            writer.WriteEndElement();
        }
    }
}