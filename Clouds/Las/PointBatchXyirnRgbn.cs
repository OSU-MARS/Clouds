using System;

namespace Mars.Clouds.Las
{
    public class PointBatchXyirnRgbn : PointBatchXy
    {
        public UInt16[] Intensity { get; private init; }
        public byte[] ReturnNumber { get; private init; }
        public UInt16[] Red { get; private init; }
        public UInt16[] Green { get; private init; }
        public UInt16[] Blue { get; private init; }
        public UInt16[] NearInfrared { get; private init; }
        public float[] ScanAngleInDegrees { get; private init; }

        public PointBatchXyirnRgbn()
            : this(PointBatch.DefaultCapacity)
        {
        }

        public PointBatchXyirnRgbn(int capacity)
            : base(capacity)
        {
            // assume capacity is larger than the ~100 elements where uninitialized becomes faster than new
            this.Intensity = GC.AllocateUninitializedArray<UInt16>(capacity);
            this.ReturnNumber = GC.AllocateUninitializedArray<byte>(capacity);
            this.Red = GC.AllocateUninitializedArray<UInt16>(capacity);
            this.Green = GC.AllocateUninitializedArray<UInt16>(capacity);
            this.Blue = GC.AllocateUninitializedArray<UInt16>(capacity);
            this.NearInfrared = GC.AllocateUninitializedArray<UInt16>(capacity);
            this.ScanAngleInDegrees = GC.AllocateUninitializedArray<float>(capacity);
        }

        public override int Capacity
        {
            get { return this.X.Length; }
        }
    }
}
