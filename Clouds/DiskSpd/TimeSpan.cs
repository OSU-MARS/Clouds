using System;
using System.Collections.Generic;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class TimeSpan : XmlSerializable
    {
        public float TestTimeSeconds { get; private set; }
        public int ThreadCount { get; private set; }
        public int RequestCount { get; private set; }
        public int ProcCount { get; private set; }
        public CpuUtilization CpuUtilization { get; private init; }
        public Latency Latency { get; private init; }
        public Iops Iops { get; private init; }
        public List<Thread> Threads { get; private init; }

        public TimeSpan()
        {
            this.TestTimeSeconds = Single.NaN;
            this.ThreadCount = -1;
            this.RequestCount = -1;
            this.ProcCount = -1;
            this.CpuUtilization = new();
            this.Latency = new();
            this.Iops = new();
            this.Threads = [];
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException($"Encountered unexpected attributes on element '{reader.Name}'.");
            }

            switch (reader.Name)
            {
                case "TimeSpan":
                    reader.Read();
                    break;
                case "TestTimeSeconds":
                    this.TestTimeSeconds = reader.ReadElementContentAsFloat();
                    break;
                case "ThreadCount":
                    this.ThreadCount = reader.ReadElementContentAsInt();
                    break;
                case "RequestCount":
                    this.RequestCount = reader.ReadElementContentAsInt();
                    break;
                case "ProcCount":
                    this.ProcCount = reader.ReadElementContentAsInt();
                    break;
                case "CpuUtilization":
                    this.CpuUtilization.ReadXml(reader);
                    break;
                case "Latency":
                    this.Latency.ReadXml(reader);
                    break;
                case "Iops":
                    this.Iops.ReadXml(reader);
                    break;
                case "Thread":
                    Thread thread = new();
                    thread.ReadXml(reader);
                    this.Threads.Add(thread);
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
