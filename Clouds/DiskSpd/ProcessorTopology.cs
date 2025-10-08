using System.Collections.Generic;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class ProcessorTopology : XmlSerializable
    {
        public ProcessorTopologyGroup Group { get; private init; }
        public ProcessorTopologyNode Node { get; private init; }
        public ProcessorSocket Socket { get; private init; }
        public List<HyperThread> HyperThreads { get; private init; }

        public ProcessorTopology()
        {
            this.Group = new();
            this.Node = new();
            this.Socket = new();
            this.HyperThreads = [];
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            switch (reader.Name)
            {
                case "ProcessorTopology":
                    if (reader.AttributeCount != 0)
                    {
                        throw new XmlException($"Encountered unexpected attributes on element '{reader.Name}'.");
                    }
                    reader.Read();
                    break;
                case "Group":
                    this.Group.ReadXml(reader);
                    break;
                case "Node":
                    this.Node.ReadXml(reader);
                    break;
                case "Socket":
                    this.Socket.ReadXml(reader);
                    break;
                case "HyperThread":
                    HyperThread thread = new();
                    thread.ReadXml(reader);
                    this.HyperThreads.Add(thread);
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
