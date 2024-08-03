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
        public int TilesLoaded { get; set; }
        public int TilesIntersected { get; private init; }

        // values of points in cell
        public List<PointClassification> Classification { get; set; }
        public List<UInt16> Intensity { get; private init; }
        public List<byte> ReturnNumber { get; private init; }
        public List<float> Z { get; private init; }

        // x and y indices in destination raster (grid metrics or DSM tile)
        public int XIndex { get; private init; }
        public int YIndex { get; private init; }

        public PointListZirnc(int xIndex, int yIndex, int tilesIntersected)
        {
            this.XIndex = xIndex;
            this.YIndex = yIndex;
            this.TilesLoaded = 0;
            this.TilesIntersected = tilesIntersected;

            this.Classification = [];
            this.Intensity = [];
            this.ReturnNumber = [];
            this.Z = [];
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

        public void ClearAndRelease()
        {
            this.TilesLoaded = 0;

            this.Classification.Clear();
            this.Classification.Capacity = 0;
            this.Intensity.Clear();
            this.Intensity.Capacity = 0;
            this.ReturnNumber.Clear();
            this.ReturnNumber.Capacity = 0;
            this.Z.Clear();
            this.Z.Capacity = 0;
            // no changes to this.TilesIntersected, XMax, XMin, YMax, YMin
        }
    }
}
