using System;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class Tool : XmlSerializable
    {
        public string Version { get; private set; }
        public string VersionDate {  get; private set; }

        public Tool()
        {
            this.Version = String.Empty;
            this.VersionDate = String.Empty;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException($"Encountered unexpected attributes on element '{reader.Name}'.");
            }

            switch (reader.Name)
            {
                case "Tool":
                    reader.Read();
                    break;
                case "Version":
                    this.Version = reader.ReadElementContentAsString();
                    break;
                case "VersionDate":
                    this.VersionDate = reader.ReadElementContentAsString(); // can be parsed if needed
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}