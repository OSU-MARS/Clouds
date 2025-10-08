using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class Cpu : CpuAverage
    {
        public int Group { get; private set; }
        public int ID { get; private set; }

        public Cpu()
        {
            this.Group = -1;
            this.ID = -1;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException($"Encountered unexpected attributes on element '{reader.Name}'.");
            }

            switch (reader.Name)
            {
                case "CPU":
                    reader.Read();
                    break;
                case "Group":
                    this.Group = reader.ReadElementContentAsInt();
                    break;
                case "Id":
                    this.ID = reader.ReadElementContentAsInt();
                    break;
                case "UsagePercent":
                    this.UsagePercent = reader.ReadElementContentAsFloat();
                    break;
                case "UserPercent":
                    this.UserPercent = reader.ReadElementContentAsFloat();
                    break;
                case "KernelPercent":
                    this.KernelPercent = reader.ReadElementContentAsFloat();
                    break;
                case "IdlePercent":
                    this.IdlePercent = reader.ReadElementContentAsFloat();
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
