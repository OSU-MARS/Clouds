namespace Mars.Clouds.GdalExtensions
{
    public class LinearCoordinateTransform
    {
        public double RotationXYinRadians { get; init; }
        public double TranslationX { get; init; }
        public double TranslationY { get; init; }
        public double TranslationZ { get; init; }

        public LinearCoordinateTransform()
        {
            this.RotationXYinRadians = 0.0;
            this.TranslationX = 0.0;
            this.TranslationY = 0.0;
            this.TranslationZ = 0.0;
        }
    }
}
