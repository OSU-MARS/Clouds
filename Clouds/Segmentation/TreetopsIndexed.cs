using Mars.Clouds.Extensions;

namespace Mars.Clouds.Segmentation
{
    public class TreetopsIndexed : Treetops
    {
        public int[] XIndex { get; private set; }
        public int[] YIndex { get; private set; }

        public TreetopsIndexed(int treetopCapacity)
            : base(treetopCapacity)
        {
            this.XIndex = new int[treetopCapacity];
            this.YIndex = new int[treetopCapacity];
        }

        public override void Extend(int newCapacity)
        {
            base.Extend(newCapacity);

            this.XIndex = this.XIndex.Extend(newCapacity);
            this.YIndex = this.YIndex.Extend(newCapacity);
        }
    }
}
