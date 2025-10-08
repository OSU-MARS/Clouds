using System.IO;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class Results : XmlSerializable
    {
        public System System { get; private init; }
        public Profile Profile { get; private init; }
        public TimeSpan TimeSpan { get; private init; }

        public Results(string resultsFilePath)
        {
            this.System = new();
            this.Profile = new();
            this.TimeSpan = new();

            using FileStream stream = new(resultsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using XmlReader reader = XmlReader.Create(stream);
            reader.MoveToContent();
            this.ReadXml(reader);
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException($"Encountered unexpected attributes on element '{reader.Name}'.");
            }

            switch (reader.Name)
            {
                case "Results":
                    reader.Read();
                    break;
                case "System":
                    this.System.ReadXml(reader);
                    break;
                case "Profile":
                    this.Profile.ReadXml(reader);
                    break;
                case "TimeSpan":
                    this.TimeSpan.ReadXml(reader);
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
