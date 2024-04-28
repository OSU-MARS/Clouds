using Mars.Clouds.Extensions;
using System;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    public class SourceFilename : XmlSerializable
    {
        // not implemented: @shared
        public bool RelativeToVrt { get; set; }
        public string Filename { get; set; }

        public SourceFilename()
        {
            this.RelativeToVrt = false;
            this.Filename = String.Empty;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 1)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "SourceFilename":
                    this.RelativeToVrt = reader.ReadAttributeAsBoolean("relativeToVRT"); // or relativetoVRT if needed for backwards compatability
                    this.Filename = reader.ReadElementContentAsString();
                    reader.Read();
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteStartElement("SourceFilename");
            writer.WriteAttributeBinary("relativeToVRT", this.RelativeToVrt);
            writer.WriteString(this.Filename);
            writer.WriteEndElement();
        }
    }
}