namespace Mars.Clouds.GdalExtensions
{
    public class CoordinateTransform
    {
        public double RotationXYinRadians { get; init; }
        public double TranslationX { get; init; }
        public double TranslationY { get; init; }
        public double TranslationZ { get; init; }

        public CoordinateTransform()
        {
            this.RotationXYinRadians = 0.0;
            this.TranslationX = 0.0;
            this.TranslationY = 0.0;
            this.TranslationZ = 0.0;
        }

        public bool HasRotationXY
        {
            get { return this.RotationXYinRadians != 0.0; } // could also check for multiples of 2π but this isn't currently needed}
        }

        public bool HasTranslationXY
        {
            get { return (this.TranslationX != 0.0) || (this.TranslationY != 0.0) || (this.TranslationZ != 0.0); }
        }

        public bool HasTranslationZ
        {
            get { return this.TranslationZ != 0.0; }
        }
    }
}
