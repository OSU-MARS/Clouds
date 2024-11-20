using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// List of points with z, intensity (I), return number (RN), and classification (C; ZIRNC).
    /// </summary>
    public class PointListZirnc
    {
        // values of points in cell
        public List<PointClassification> Classification { get; private init; }
        public List<UInt16> Intensity { get; private init; }
        public List<byte> ReturnNumber { get; private init; }
        public List<float> Z { get; private init; }

        public int TilesLoaded { get; set; }
        public int TilesIntersected { get; set; }

        public PointListZirnc()
        {
            this.Classification = [];
            this.Intensity = [];
            this.ReturnNumber = [];
            this.Z = [];

            this.TilesLoaded = 0;
            this.TilesIntersected = 1;
        }

        public int Capacity
        {
            get
            {
                Debug.Assert((this.Classification.Capacity == this.Intensity.Capacity) && (this.Classification.Capacity == this.Intensity.Capacity) && (this.Classification.Capacity == this.Intensity.Capacity) && (this.Classification.Capacity == this.Z.Capacity));
                return this.Classification.Capacity;
            }
            set
            {
                this.Classification.Capacity = value;
                this.Intensity.Capacity = value;
                this.ReturnNumber.Capacity = value;
                this.Z.Capacity = value;
            }
        }

        public int Count
        {
            get
            {
                Debug.Assert((this.Classification.Count == this.Intensity.Count) && (this.Classification.Count == this.Intensity.Count) && (this.Classification.Count == this.Intensity.Count) && (this.Classification.Count == this.Z.Count));
                return this.Classification.Count;
            }
        }

        public bool AddPointsFromTile(PointListZirnc other)
        {
            this.Classification.AddRange(other.Classification);
            this.Intensity.AddRange(other.Intensity);
            this.ReturnNumber.AddRange(other.ReturnNumber);
            this.Z.AddRange(other.Z);

            ++this.TilesLoaded;
            Debug.Assert(this.TilesLoaded <= this.TilesIntersected);
            return this.TilesLoaded == this.TilesIntersected;
        }

        public void Reset()
        {
            this.Classification.Clear();
            this.Intensity.Clear();
            this.ReturnNumber.Clear();
            this.Z.Clear();

            this.TilesIntersected = 1;
            this.TilesLoaded = 0;
        }
    }
}
