using System;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class ThreadTarget : XmlSerializable
    {
        public string Path { get; private set; }
        public long BytesCount { get; private set; }
        public long FileSize { get; private set; }
        public long IOCount { get; private set; }
        public long ReadBytes { get; private set; }
        public long ReadCount { get; private set; }
        public long WriteBytes { get; private set; }
        public long WriteCount { get; private set; }
        public float AverageReadLatencyMilliseconds { get; private set; }
        public float ReadLatencyStdev { get; private set; }
        public float AverageWriteLatencyMilliseconds { get; private set; }
        public float WriteLatencyStdev { get; private set; }
        public float AverageLatencyMilliseconds { get; private set; }
        public float LatencyStdev { get; private set; }
        public Iops Iops { get; private init; }

        public ThreadTarget()
        {
            this.Path = String.Empty;
            this.BytesCount = -1;
            this.FileSize = -1;
            this.IOCount = -1;
            this.ReadBytes = -1;
            this.ReadCount = -1;
            this.WriteBytes = -1;
            this.WriteCount = -1;
            this.AverageReadLatencyMilliseconds = Single.NaN;
            this.ReadLatencyStdev = Single.NaN;
            this.AverageWriteLatencyMilliseconds = Single.NaN;
            this.WriteLatencyStdev = Single.NaN;
            this.AverageLatencyMilliseconds = Single.NaN;
            this.LatencyStdev = Single.NaN;
            this.Iops = new();
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "Target":
                    reader.Read();
                    break;
                case "Path":
                    this.Path = reader.ReadElementContentAsString();
                    break;
                case "BytesCount":
                    this.BytesCount = reader.ReadElementContentAsLong();
                    break;
                case "FileSize":
                    this.FileSize = reader.ReadElementContentAsLong();
                    break;
                case "IOCount":
                    this.IOCount = reader.ReadElementContentAsLong();
                    break;
                case "ReadBytes":
                    this.ReadBytes = reader.ReadElementContentAsLong();
                    break;
                case "ReadCount":
                    this.ReadCount = reader.ReadElementContentAsLong();
                    break;
                case "WriteBytes":
                    this.WriteBytes = reader.ReadElementContentAsLong();
                    break;
                case "WriteCount":
                    this.WriteCount = reader.ReadElementContentAsLong();
                    break;
                case "AverageReadLatencyMilliseconds":
                    this.AverageReadLatencyMilliseconds = reader.ReadElementContentAsFloat();
                    break;
                case "ReadLatencyStdev":
                    this.ReadLatencyStdev = reader.ReadElementContentAsFloat();
                    break;
                case "AverageWriteLatencyMilliseconds":
                    this.AverageWriteLatencyMilliseconds = reader.ReadElementContentAsFloat();
                    break;
                case "WriteLatencyStdev":
                    this.WriteLatencyStdev = reader.ReadElementContentAsFloat();
                    break;
                case "AverageLatencyMilliseconds":
                    this.AverageLatencyMilliseconds = reader.ReadElementContentAsFloat();
                    break;
                case "LatencyStdev":
                    this.LatencyStdev = reader.ReadElementContentAsFloat();
                    break;
                case "Iops":
                    this.Iops.ReadXml(reader);
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
