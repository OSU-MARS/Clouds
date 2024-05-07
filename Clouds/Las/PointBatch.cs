namespace Mars.Clouds.Las
{
    public abstract class PointBatch
    {
        public const int DefaultCapacity = 10000000; // 10 Mpoints

        public int Count { get; set; }

        protected PointBatch() 
        { 
            this.Count = 0;
        }

        public abstract int Capacity { get; }

        public void Clear()
        {
            this.Count = 0;
        }
    }
}
