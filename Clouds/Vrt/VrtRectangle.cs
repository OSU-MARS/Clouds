using Mars.Clouds.Extensions;
using System;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    public class VrtRectangle : XmlSerializable
    {
        private double xSize;
        private double ySize;

        public double XOffset { get; set; }
        public double YOffset { get; set; }

        public VrtRectangle()
        {
            this.xSize = Double.NaN;
            this.ySize = Double.NaN;

            this.XOffset = Double.NaN;
            this.YOffset = Double.NaN;
        }

        public double XSize 
        { 
            get { return this.xSize; }
            set
            {
                if (value <= 0.0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Rectangle's x size cannot be zero or negative.");
                }
                this.xSize = value;
            }
        }

        public double YSize
        {
            get { return this.ySize; }
            set
            {
                if (value <= 0.0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Rectangle's y size cannot be zero or negative.");
                }
                this.ySize = value;
            }
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 4)
            {
                throw new XmlException($"Encountered unexpected attributes on element '{reader.Name}'.");
            }

            switch (reader.Name)
            {
                case "DstRect":
                case "SrcRect":
                    this.XOffset = reader.ReadAttributeAsDouble("xOff");
                    this.YOffset = reader.ReadAttributeAsDouble("yOff");
                    this.XSize = reader.ReadAttributeAsDouble("xSize");
                    this.YSize = reader.ReadAttributeAsDouble("ySize");
                    reader.Read();
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }

        public void WriteXml(XmlWriter writer, string localName)
        {
            writer.WriteStartElement(localName);
            writer.WriteAttributeDoubleOrNan("xOff", this.XOffset);
            writer.WriteAttributeDoubleOrNan("yOff", this.YOffset);
            writer.WriteAttributeDoubleOrNan("xSize", this.XSize);
            writer.WriteAttributeDoubleOrNan("ySize", this.YSize);
            writer.WriteEndElement();
        }
    }
}