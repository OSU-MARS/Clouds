using System;
using System.Collections.Generic;
using System.Xml;

namespace Mars.Clouds.DiskSpd
{
    public class ProfileTarget : XmlSerializable
    {
        public string Path { get; private set; }
        public int BlockSize { get; private set; }
        public int BaseFileOffset { get; private set; }
        public bool SequentialScan { get; private set; }
        public bool RandomAccess { get; private set; }
        public bool TemporaryFile { get; private set; }
        public bool UseLargePages { get; private set; }
        public bool DisableOSCache { get; private set; }
        public bool WriteThrough { get; private set; }
        public bool ParallelAsyncIO { get; private set; }
        public int StrideSize { get; private set; }
        public bool InterlockedSequential { get; private set; }
        public int Random { get; private set; }
        public int ThreadStride { get; private set; }
        public int MaxFileSize { get; private set; }
        public int RequestCount { get; private set; }
        public int WriteRatio { get; private set; }
        public int Throughput { get; private set; }
        public int ThreadsPerFile { get; private set; }
        public int IOPriority { get; private set; }
        public int Weight { get; private set; }
        public List<string> WriteBufferContent { get; private init; }

        public ProfileTarget()
        {
            this.Path = String.Empty;
            this.BlockSize = -1;
            this.BaseFileOffset = -1;
            this.SequentialScan = false;
            this.RandomAccess = false;
            this.TemporaryFile = false;
            this.UseLargePages = false;
            this.DisableOSCache = false;
            this.WriteThrough = false;
            this.ParallelAsyncIO = false;
            this.Random = -1;
            this.ThreadStride = -1;
            this.MaxFileSize = -1;
            this.RequestCount = -1;
            this.WriteRatio = -1;
            this.Throughput = -1;
            this.ThreadsPerFile = -1;
            this.IOPriority = -1;
            this.Weight = -1;
            this.WriteBufferContent = [];
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
                case "BlockSize":
                    this.BlockSize = reader.ReadElementContentAsInt();
                    break;
                case "BaseFileOffset":
                    this.BaseFileOffset = reader.ReadElementContentAsInt();
                    break;
                case "SequentialScan":
                    this.SequentialScan = reader.ReadElementContentAsBoolean();
                    break;
                case "TemporaryFile":
                    this.TemporaryFile = reader.ReadElementContentAsBoolean();
                    break;
                case "RandomAccess":
                    this.RandomAccess = reader.ReadElementContentAsBoolean();
                    break;
                case "UseLargePages":
                    this.UseLargePages = reader.ReadElementContentAsBoolean();
                    break;
                case "DisableOSCache":
                    this.DisableOSCache = reader.ReadElementContentAsBoolean();
                    break;
                case "WriteThrough":
                    this.WriteThrough = reader.ReadElementContentAsBoolean();
                    break;
                case "ParallelAsyncIO":
                    this.ParallelAsyncIO = reader.ReadElementContentAsBoolean();
                    break;
                case "StrideSize":
                    this.StrideSize = reader.ReadElementContentAsInt();
                    break;
                case "InterlockedSequential":
                    this.InterlockedSequential = reader.ReadElementContentAsBoolean();
                    break;
                case "Random":
                    this.Random = reader.ReadElementContentAsInt();
                    break;
                case "Pattern":
                    this.WriteBufferContent.Add(reader.ReadElementContentAsString());
                    break;
                case "ThreadStride":
                    this.ThreadStride = reader.ReadElementContentAsInt();
                    break;
                case "MaxFileSize":
                    this.MaxFileSize = reader.ReadElementContentAsInt();
                    break;
                case "RequestCount":
                    this.RequestCount = reader.ReadElementContentAsInt();
                    break;
                case "WriteRatio":
                    this.WriteRatio = reader.ReadElementContentAsInt();
                    break;
                case "Throughput":
                    this.Throughput = reader.ReadElementContentAsInt();
                    break;
                case "ThreadsPerFile":
                    this.ThreadsPerFile = reader.ReadElementContentAsInt();
                    break;
                case "IOPriority":
                    this.IOPriority = reader.ReadElementContentAsInt();
                    break;
                case "Weight":
                    this.Weight = reader.ReadElementContentAsInt();
                    break;
                case "WriteBufferContent":
                    reader.Read();
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }
    }
}
