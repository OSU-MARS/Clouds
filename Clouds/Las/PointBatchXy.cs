using System;

namespace Mars.Clouds.Las
{
    public class PointBatchXy : PointBatch
    {
        public int[] X { get; private init; }
        public int[] Y { get; private init; }

        public PointBatchXy(int capacity)
            : base()
        {
            // assume capacity is larger than the ~100 elements where uninitialized becomes faster than new
            this.X = GC.AllocateUninitializedArray<int>(capacity);
            this.Y = GC.AllocateUninitializedArray<int>(capacity);
        }

        public override int Capacity
        {
            get { return this.X.Length; }
        }
    }
}
