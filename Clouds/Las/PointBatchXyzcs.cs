using System;

namespace Mars.Clouds.Las
{
    public class PointBatchXyzcs : PointBatchXy
    {
        public int[] Z { get; private init; }
        public PointClassification[] Classification { get; private init; }
        public UInt16[] SourceID { get; private init; }

        public PointBatchXyzcs()
            : this(PointBatch.DefaultCapacity)
        {
        }

        public PointBatchXyzcs(int capacity)
            : base(capacity)
        {
            this.Z = GC.AllocateUninitializedArray<int>(capacity);
            this.Classification = GC.AllocateUninitializedArray<PointClassification>(capacity);
            this.SourceID = GC.AllocateUninitializedArray<UInt16>(capacity);
        }
    }
}
