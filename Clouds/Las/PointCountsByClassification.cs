using System;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.Las
{
    public class PointCountsByClassification
    {
        public string FilePath { get; set; }
        public UInt64 Ground { get; private set; }
        public UInt64 HighNoise { get; private set; }
        public UInt64 HighVegetation { get; private set; }
        public UInt64 LowNoise { get; private set; }
        public UInt64 LowVegetation { get; private set; }
        public UInt64 MediumVegetation { get; private set; }
        public UInt64 NeverClassified { get; private set; }
        public UInt64 Other { get; private set; }
        public UInt64 Unclassified { get; private set; }
        public UInt64 Withdrawn { get; private set; }

        public PointCountsByClassification(string filePath)
        {
            this.FilePath = filePath;
            this.Ground = 0;
            this.HighNoise = 0;
            this.HighVegetation = 0;
            this.LowNoise = 0;
            this.LowVegetation = 0;
            this.MediumVegetation = 0;
            this.NeverClassified = 0;
            this.Other = 0;
            this.Unclassified = 0;
            this.Withdrawn = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(PointClassification point)
        {
            switch (point)
            {
                case PointClassification.Ground:
                    ++this.Ground;
                    break;
                case PointClassification.HighNoise:
                    ++this.HighNoise;
                    break;
                case PointClassification.HighVegetation:
                    ++this.HighVegetation;
                    break;
                case PointClassification.LowNoise:
                    ++this.LowNoise;
                    break;
                case PointClassification.LowVegetation:
                    ++this.LowVegetation;
                    break;
                case PointClassification.MediumVegetation:
                    ++this.MediumVegetation;
                    break;
                case PointClassification.NeverClassified:
                    ++this.NeverClassified;
                    break;
                case PointClassification.Unclassified:
                    ++this.Unclassified;
                    break;
                default:
                    ++this.Other;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWithdrawn()
        {
            ++this.Withdrawn;
        }
    }
}
