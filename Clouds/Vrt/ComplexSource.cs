using Mars.Clouds.Extensions;
using System;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    public class ComplexSource : /* SimpleSource : ArraySource */ XmlSerializable
    {
        // not implemented: ScaleOffset, ScaleRatio, ColorTableComponent, Exponent, SrcMin, SrcMax, DstMin, DstMax, UseMaskBand, LUT
        public int SourceBand { get; set; }
        public SourceFilename SourceFilename { get; private init; }
        public SourceProperties SourceProperties { get; private init; }
        public VrtRectangle SourceRectangle { get; private init; }
        public VrtRectangle DestinationRectangle { get; private init; }
        public double NoDataValue { get; set; }

        public ComplexSource()
        {
            this.SourceBand = -1;
            this.SourceFilename = new();
            this.SourceProperties = new();
            this.SourceRectangle = new();
            this.DestinationRectangle = new();
            this.NoDataValue = Double.NaN;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount == 0)
            {
                switch (reader.Name)
                {
                    case "ComplexSource":
                        reader.Read();
                        break;
                    case "SourceBand":
                        this.SourceBand = reader.ReadElementContentAsInt();
                        break;
                    case "NODATA":
                        this.NoDataValue = reader.ReadElementContentAsDouble();
                        break;
                    default:
                        throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
                }
            }
            else
            {
                switch (reader.Name)
                {
                    case "SourceFilename":
                        this.SourceFilename.ReadXml(reader);
                        break;
                    case "SourceProperties":
                        this.SourceProperties.ReadXml(reader);
                        break;
                    case "SrcRect":
                        this.SourceRectangle.ReadXml(reader);
                        break;
                    case "DstRect":
                        this.DestinationRectangle.ReadXml(reader);
                        break;
                    default:
                        throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
                }
            }
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteStartElement("ComplexSource");
            this.SourceFilename.WriteXml(writer);
            writer.WriteElementString("SourceBand", this.SourceBand);
            this.SourceProperties.WriteXml(writer);
            this.SourceRectangle.WriteXml(writer, "SrcRect");
            this.DestinationRectangle.WriteXml(writer, "DstRect");
            writer.WriteElementDoubleOrNaN("NODATA", this.NoDataValue);
            writer.WriteEndElement();
        }
    }
}