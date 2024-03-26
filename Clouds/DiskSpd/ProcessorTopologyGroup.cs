using System;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class ProcessorTopologyGroup : XmlSerializable
    {
        public int Group { get; private set; }
        public int MaximumProcessors { get; private set; }
        public int ActiveProcessors { get; private set; }
        public UInt32 ActiveProcessorMask { get; private set; }

        public ProcessorTopologyGroup()
        {
            this.Group = -1;
            this.MaximumProcessors = -1;
            this.ActiveProcessors = -1;
            this.ActiveProcessorMask = 0x0;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 4)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "Group":
                    this.Group = XmlSerializable.ReadAttributeAsInt32(reader, "Group");
                    this.MaximumProcessors = XmlSerializable.ReadAttributeAsInt32(reader, "MaximumProcessors");
                    this.ActiveProcessors = XmlSerializable.ReadAttributeAsInt32(reader, "ActiveProcessors");
                    this.ActiveProcessorMask = XmlSerializable.ReadAttributeAsUInt32Hex(reader, "ActiveProcessorMask");
                    reader.Read();
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
