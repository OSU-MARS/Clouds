using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class Thread : XmlSerializable
    {
        public int ID { get; private set; }
        public ThreadTarget Target { get; private init; }

        public Thread()
        {
            this.ID = -1;
            this.Target = new();
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "Thread":
                    reader.Read();
                    break;
                case "Id":
                    this.ID = reader.ReadElementContentAsInt();
                    break;
                case "Target":
                    this.Target.ReadXml(reader);
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
