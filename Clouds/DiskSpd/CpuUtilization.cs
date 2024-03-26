using System.Collections.Generic;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class CpuUtilization : XmlSerializable
    {
        public List<Cpu> Cpus { get; private init; }
        public CpuAverage Average { get; private init; }

        public CpuUtilization() 
        { 
            this.Cpus = [];
            this.Average = new();
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "CpuUtilization":
                    reader.Read();
                    break;
                case "CPU":
                    Cpu cpu = new();
                    cpu.ReadXml(reader);
                    this.Cpus.Add(cpu);
                    break;
                case "Average":
                    this.Average.ReadXml(reader);
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
