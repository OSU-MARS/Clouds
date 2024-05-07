using System;

namespace Mars.Clouds.Las
{
    public class PointBatchXyzcs : PointBatch
    {
        public int[] X { get; private init; }
        public int[] Y { get; private init; }
        public int[] Z { get; private init; }
        public PointClassification[] Classification { get; private init; }
        public UInt16[] SourceID { get; private init; }

        public PointBatchXyzcs()
            : this(PointBatch.DefaultCapacity)
        {
        }

        public PointBatchXyzcs(int capacity)
            : base()
        {
            // assume capacity is larger than the ~100 elements where uninitialized becomes faster than new
            this.X = GC.AllocateUninitializedArray<int>(capacity);
            this.Y = GC.AllocateUninitializedArray<int>(capacity);
            this.Z = GC.AllocateUninitializedArray<int>(capacity);
            this.Classification = GC.AllocateUninitializedArray<PointClassification>(capacity);
            this.SourceID = GC.AllocateUninitializedArray<UInt16>(capacity);
        }

        public override int Capacity
        { 
            get { return this.X.Length; } 
        }
    }
}
