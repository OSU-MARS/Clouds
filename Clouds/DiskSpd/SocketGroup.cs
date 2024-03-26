using System;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class SocketGroup : XmlSerializable
    {
        public int Group { get; private set; }
        public UInt32 Processors { get; private set; }

        public SocketGroup()
        {
            this.Group = -1;
            this.Processors = 0x0;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 2)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "Group":
                    this.Group = XmlSerializable.ReadAttributeAsInt32(reader, "Group");
                    this.Processors = XmlSerializable.ReadAttributeAsUInt32Hex(reader, "Processors");
                    reader.Read();
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
