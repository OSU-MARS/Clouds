using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// List of points with z, intensity (I), return number (RN), and classification (C; ZIRNC).
    /// </summary>
    public class PointListZirnc : PointListZ
    {
        public int TilesLoaded { get; set; }
        public int TilesIntersected { get; private init; }

        // values of points in cell
        public List<PointClassification> Classification { get; set; }
        public List<UInt16> Intensity { get; private init; }
        public List<byte> ReturnNumber { get; private init; }

        // range of points within the cell
        public double PointXMax { get; set; }
        public double PointXMin { get; set; }
        public double PointYMax { get; set; }
        public double PointYMin { get; set; }

        public PointListZirnc(int xIndex, int yIndex, int tilesIntersected)
            : base(xIndex, yIndex)
        {
            this.TilesLoaded = 0;
            this.TilesIntersected = tilesIntersected;

            this.Classification = [];
            this.Intensity = [];
            this.ReturnNumber = [];
            this.PointXMax = Double.MinValue;
            this.PointXMin = Double.MaxValue;
            this.PointYMax = Double.MinValue;
            this.PointYMin = Double.MaxValue;
        }

        public override int Capacity
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

        public override void ClearAndRelease()
        {
            base.ClearAndRelease();
            this.TilesLoaded = 0;

            this.Classification.Clear();
            this.Classification.Capacity = 0;
            this.Intensity.Clear();
            this.Intensity.Capacity = 0;
            this.ReturnNumber.Clear();
            this.ReturnNumber.Capacity = 0;
            // no changes to this.TilesIntersected, XMax, XMin, YMax, YMin
        }
    }
}
