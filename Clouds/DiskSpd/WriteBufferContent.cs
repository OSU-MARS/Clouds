using System;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class WriteBufferContent : XmlSerializable
    {
        public string Pattern { get; private set; }

        public WriteBufferContent()
        {
            this.Pattern = String.Empty;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException($"Encountered unexpected attributes on element '{reader.Name}'.");
            }

            switch (reader.Name)
            {
                case "WriteBufferContent":
                    reader.Read();
                    break;
                case "Pattern":
                    this.Pattern = reader.ReadElementContentAsString();
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
