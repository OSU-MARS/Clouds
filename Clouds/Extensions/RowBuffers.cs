namespace Mars.Clouds.Extensions
{
    public class RowBuffers
    {
        public float[] RowBuffer1 { get; set; }
        public float[] RowBuffer2 { get; set; }
        public float[] RowBuffer3 { get; set; }

        public RowBuffers(int sizeX)
        {
            this.RowBuffer1 = new float[sizeX];
            this.RowBuffer2 = new float[sizeX];
            this.RowBuffer3 = new float[sizeX];
        }
    }
}
