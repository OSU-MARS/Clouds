using System.Collections.Generic;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class ProfileTimeSpan : XmlSerializable
    {
        public bool CompletionRoutines { get; private set; }
        public bool MeasureLatency { get; private set; }
        public bool CalculateIopsStdDev { get; private set; }
        public bool DisableAffinity { get; private set; }
        public int Warmup { get; private set; }
        public int Duration { get; private set; }
        public int Cooldown { get; private set; }
        public int ThreadCount { get; private set; }
        public int RequestCount { get; private set; }
        public int IoBucketDuration { get; private set; }
        public int RandSeed { get; private set; }
        public List<ProfileTarget> Targets { get; private init; }

        public ProfileTimeSpan()
        {
            this.CompletionRoutines = false;
            this.MeasureLatency = false;
            this.CalculateIopsStdDev = false;
            this.DisableAffinity = false;
            this.Duration = -1;
            this.Warmup = -1;
            this.Cooldown = -1;
            this.ThreadCount = -1;
            this.RequestCount = -1;
            this.IoBucketDuration = -1;
            this.RandSeed = -1;
            this.Targets = [];
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
                case "CompletionRoutines":
                    this.CompletionRoutines = reader.ReadElementContentAsBoolean();
                    break;
                case "MeasureLatency":
                    this.MeasureLatency = reader.ReadElementContentAsBoolean();
                    break;
                case "CalculateIopsStdDev":
                    this.CalculateIopsStdDev = reader.ReadElementContentAsBoolean();
                    break;
                case "DisableAffinity":
                    this.DisableAffinity = reader.ReadElementContentAsBoolean();
                    break;
                case "Duration":
                    this.Duration = reader.ReadElementContentAsInt();
                    break;
                case "Warmup":
                    this.Warmup = reader.ReadElementContentAsInt();
                    break;
                case "Cooldown":
                    this.Cooldown = reader.ReadElementContentAsInt();
                    break;
                case "ThreadCount":
                    this.ThreadCount = reader.ReadElementContentAsInt();
                    break;
                case "RequestCount":
                    this.RequestCount = reader.ReadElementContentAsInt();
                    break;
                case "IoBucketDuration":
                    this.IoBucketDuration = reader.ReadElementContentAsInt();
                    break;
                case "RandSeed":
                    this.RandSeed = reader.ReadElementContentAsInt();
                    break;
                case "Target":
                    ProfileTarget target = new();
                    target.ReadXml(reader);
                    this.Targets.Add(target);
                    break;
                case "Targets":
                    reader.Read();
                    break;
                default:
                    throw new XmlException($"Element '{reader.Name}' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
