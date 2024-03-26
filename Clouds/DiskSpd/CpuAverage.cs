using System;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class CpuAverage : XmlSerializable
    {
        public float UsagePercent { get; protected set; }
        public float UserPercent { get; protected set; }
        public float KernelPercent { get; protected set; }
        public float IdlePercent { get; protected set; }

        public CpuAverage()
        {
            this.UsagePercent = Single.NaN;
            this.UserPercent = Single.NaN;
            this.KernelPercent = Single.NaN;
            this.IdlePercent = Single.NaN;
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "Average":
                    reader.Read();
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
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
