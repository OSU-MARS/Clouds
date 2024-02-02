namespace Mars.Clouds.GdalExtensions
{
    public class Extent
    {
        public double XMin { get; set; }
        public double XMax { get; set; }
        public double YMin { get; set; }
        public double YMax { get; set; }

        public Extent()
        {
            // leave fields at default of zero
        }

        public Extent(Grid grid)
        {
            (this.XMin, this.XMax, this.YMin, this.YMax) = grid.GetExtent();
        }

        public Extent(double xMin, double xMax, double yMin, double yMax)
        {
            this.XMin = xMin;
            this.XMax = xMax;
            this.YMin = yMin;
            this.YMax = yMax;
        }

        public double Height
        {
            get { return this.YMax - this.YMin; }
        }

        public double Width
        {
            get { return this.XMax - this.XMin; }
        }

        public (double xCentroid, double yCentroid) GetCentroid()
        {
            double xCentroid = 0.5 * (this.XMin + this.XMax);
            double yCentroid = 0.5 * (this.YMin + this.YMax);
            return (xCentroid, yCentroid);
        }

        public double GetArea()
        {
            return (this.XMax - this.XMin) * (this.YMax - this.YMin);
        }

        public bool Intersects(Extent other)
        {
            // assume both this and the other extent are in the same CRS
            // find extent of intersection
            bool noIntersection = (this.XMax < other.XMin) || (this.XMin > other.XMax) || (this.YMax < other.YMin) || (this.YMin > other.YMax);
            return noIntersection == false;
        }
    }
}
