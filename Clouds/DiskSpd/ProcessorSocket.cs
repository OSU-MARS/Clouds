using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class ProcessorSocket : XmlSerializable
    {
        public SocketGroup Group { get; private init; } // might be a collection, but no multi-socket motherboards to test on (not in diskspd.xsd)

        public ProcessorSocket()
        {
            this.Group = new();
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            switch (reader.Name)
            {
                case "Socket":
                    if (reader.AttributeCount != 0)
                    {
                        throw new XmlException($"Encountered unexpected attributes on element '{reader.Name}'.");
                    }
                    reader.Read();
                    break;
                case "Group":
                    this.Group.ReadXml(reader);
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
