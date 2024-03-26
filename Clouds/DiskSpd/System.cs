using System;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class System : XmlSerializable
    {
        public string ComputerName { get; private set; }
        public Tool Tool { get; private init; }
        public string RunTime { get; private set; }
        public ProcessorTopology ProcessorTopology { get; private init; }

        public System() 
        {
            this.ComputerName = String.Empty;
            this.Tool = new();
            this.RunTime = String.Empty;
            this.ProcessorTopology = new();
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "System":
                    reader.Read();
                    break;
                case "ComputerName":
                    this.ComputerName = reader.ReadElementContentAsString();
                    break;
                case "Tool":
                    this.Tool.ReadXml(reader);
                    break;
                case "RunTime":
                    this.RunTime = reader.ReadElementContentAsString(); // can be parsed if needed
                    break;
                case "ProcessorTopology":
                    this.ProcessorTopology.ReadXml(reader);
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
