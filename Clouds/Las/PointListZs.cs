using System;
using System.Collections.Generic;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// List of aerial points with z values and point source ID plus ground elevation.
    /// </summary>
    public class PointListZs
    {
        // x and y indices in destination raster (grid metrics or DSM tile)
        public int XIndex { get; private init; }
        public int YIndex { get; private init; }

        public List<float> AerialPoints { get; private init; }
        public List<UInt16> AerialSourceIDs { get; private init; }
        public float GroundMean { get; set; }
        public UInt32 GroundPoints { get; set; }

        public PointListZs(int xIndex, int yIndex)
        {
            this.XIndex = xIndex;
            this.YIndex = yIndex;
            this.AerialPoints = [];
            this.AerialSourceIDs = [];
            this.GroundMean = Single.NaN;
            this.GroundPoints = 0;
        }

        public void OnPointAdditionComplete()
        {
            if (this.GroundPoints > 0)
            {
                this.GroundMean /= this.GroundPoints;
            }
        }
    }
}
