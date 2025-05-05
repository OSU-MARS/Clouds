using Mars.Clouds.Extensions;

namespace Mars.Clouds.Segmentation
{
    public class Treetops
    {
        public int Count { get; set; }
        public int[] ID { get; private set; }
        public double[] X { get; private set; }
        public double[] Y { get; private set; }
        public double[] Elevation { get; private set; }
        public double[] Height { get; private set; }
        public double[] Radius { get; private set; }

        protected Treetops(int treetopCapacity)
        {
            this.Count = 0;
            this.ID = new int[treetopCapacity];
            this.X = new double[treetopCapacity];
            this.Y = new double[treetopCapacity];
            this.Elevation = new double[treetopCapacity];
            this.Height = new double[treetopCapacity];
            this.Radius = new double[treetopCapacity];
        }

        public int Capacity
        {
            get { return this.ID.Length; }
        }

        public void Clear()
        {
            this.Count = 0;
        }

        public virtual void Extend(int newCapacity)
        {
            this.ID = this.ID.Extend(newCapacity);
            this.X = this.X.Extend(newCapacity);
            this.Y = this.Y.Extend(newCapacity);
            this.Elevation = this.Elevation.Extend(newCapacity);
            this.Height = this.Height.Extend(newCapacity);
            this.Radius = this.Radius.Extend(newCapacity);
        }
    }
}
