using System.Collections.Generic;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// List of points with z values.
    /// </summary>
    public class PointListZ
    {
        // x and y indices in destination raster (grid metrics or DSM tile)
        public int XIndex { get; private init; }
        public int YIndex { get; private init; }

        public List<float> Z { get; private init; }

        public PointListZ(int xIndex, int yIndex)
        {
            this.XIndex = xIndex;
            this.YIndex = yIndex;
            this.Z = [];
        }

        public virtual int Capacity
        {
            get { return this.Z.Capacity; }
            set { this.Z.Capacity = value; }
        }

        public int Count
        {
            get { return this.Z.Count; }
        }

        public virtual void ClearAndRelease()
        {
            // no changes to this.XIndex or YIndex
            this.Z.Clear();
            this.Z.Capacity = 0;
        }
    }
}
