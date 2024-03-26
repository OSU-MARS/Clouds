using System;
using System.Collections.Generic;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class Profile : XmlSerializable
    {
        public int Progress { get; private set; }
        public string ResultFormat { get; private set; }
        public bool Verbose { get; private set; }
        public List<ProfileTimeSpan> TimeSpans { get; private init; }

        public Profile()
        {
            this.Progress = -1;
            this.ResultFormat = String.Empty;
            this.Verbose = false;
            this.TimeSpans = [];
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "Profile":
                    reader.Read();
                    break;
                case "Progress":
                    this.Progress = reader.ReadElementContentAsInt();
                    break;
                case "ResultFormat":
                    this.ResultFormat = reader.ReadElementContentAsString();
                    break;
                case "Verbose":
                    this.Verbose = reader.ReadElementContentAsBoolean();
                    break;
                case "TimeSpan":
                    ProfileTimeSpan timeSpan = new();
                    timeSpan.ReadXml(reader);
                    this.TimeSpans.Add(timeSpan);
                    break;
                case "TimeSpans":
                    reader.Read();
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
