using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// Points with xyz values, classification, and point source ID plus ground elevation.
    /// </summary>
    public class PointListXyzcs
    {
        public int Count { get; set; }
        public int[] X { get; private init; }
        public int[] Y { get; private init; }
        public int[] Z { get; private init; }
        public PointClassification[] Classification { get; private init; }
        public UInt16[] SourceID { get; private init; }

        public SpatialReference Crs { get; private init; }
        public Extent GridExtent { get; private init; }
        public double XScaleFactor { get; private init; }
        public double YScaleFactor { get; private init; }
        public float ZScaleFactor { get; private init; }
        public double XOffset { get; private init; }
        public double YOffset { get; private init; }
        public float ZOffset { get; private init; }

        public PointListXyzcs(LasTile lasTile)
        {
            UInt64 pointCount = lasTile.Header.GetNumberOfPoints();
            if (pointCount > (UInt64)Array.MaxLength)
            {
                throw new ArgumentOutOfRangeException(nameof(lasTile), "Point cloud '" + lasTile.FilePath + "' contains " + pointCount.ToString("n0") + " points. This exceeds C#'s maximum array size of " + Array.MaxLength.ToString("n0") + ".");
            }

            int capacity = (int)pointCount;
            this.Count = 0;
            this.X = new int[capacity];
            this.Y = new int[capacity];
            this.Z = new int[capacity];
            this.Classification = new PointClassification[capacity];
            this.SourceID = new UInt16[capacity];

            LasHeader10 lasHeader = lasTile.Header;
            this.Crs = lasTile.GetSpatialReference();
            this.GridExtent = lasTile.GridExtent;
            this.XScaleFactor = lasHeader.XScaleFactor;
            this.YScaleFactor = lasHeader.YScaleFactor;
            this.ZScaleFactor = (float)lasHeader.ZScaleFactor;
            this.XOffset = lasHeader.XOffset;
            this.YOffset = lasHeader.YOffset;
            this.ZOffset = (float)lasHeader.ZOffset;
        }
    }
}
