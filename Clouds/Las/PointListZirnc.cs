using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// List of points with Z, intensity (I), return number (RN), and classification (C; ZIRNC).
    /// </summary>
    public class PointListZirnc
    {
        public int TilesLoaded { get; set; }
        public int TilesIntersected { get; private init; }
        // x and y indices in destination grid metrics raster
        public int XIndex { get; private init; }
        public int YIndex { get; private init; }

        // values of points in cell
        public List<PointClassification> Classification { get; set; }
        public List<UInt16> Intensity { get; private init; }
        public List<byte> ReturnNumber { get; private init; }
        public List<float> Z { get; private init; }

        // range of points within the cell
        public double PointXMax { get; set; }
        public double PointXMin { get; set; }
        public double PointYMax { get; set; }
        public double PointYMin { get; set; }

        public PointListZirnc(int xIndex, int yIndex, int tilesIntersected)
        {
            this.TilesLoaded = 0;
            this.TilesIntersected = tilesIntersected;
            this.XIndex = xIndex;
            this.YIndex = yIndex;

            this.Classification = [];
            this.Intensity = [];
            this.ReturnNumber = [];
            this.PointXMax = Double.MinValue;
            this.PointXMin = Double.MaxValue;
            this.PointYMax = Double.MinValue;
            this.PointYMin = Double.MaxValue;
            this.Z = [];
        }

        public int Capacity
        {
            get
            {
                Debug.Assert((this.Intensity.Capacity == this.ReturnNumber.Capacity) && (this.Intensity.Capacity == this.Z.Capacity));
                return this.Intensity.Capacity;
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
                Debug.Assert((this.Intensity.Count == this.ReturnNumber.Count) && (this.Intensity.Count == this.Z.Count));
                return this.Intensity.Count; 
            }
        }

        public void ClearAndRelease()
        {
            this.TilesLoaded = 0;
            // no changes to this.TilesIntersected, XIndex, YIndex, XMax, XMin, YMax, YMin

            this.Classification.Clear();
            this.Classification.Capacity = 0;
            this.Intensity.Clear();
            this.Intensity.Capacity = 0;
            this.ReturnNumber.Clear();
            this.ReturnNumber.Capacity = 0;
            this.Z.Clear();
            this.Z.Capacity = 0;
        }
    }
}
