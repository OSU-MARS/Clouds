namespace Mars.Clouds.GdalExtensions
{
    public class Neighborhood8<T> where T : class
    {
        public T Center { get; private init; }
        public T? North { get; init; }
        public T? Northeast { get; init; }
        public T? Northwest { get; init; }
        public T? South { get; init; }
        public T? Southeast { get; init; }
        public T? Southwest { get; init; }
        public T? East { get; init; }
        public T? West { get; init; }

        public Neighborhood8(T center)
        {
            this.Center = center;

            this.North = null;
            this.Northeast = null;
            this.Northwest = null;
            this.South = null;
            this.Southeast = null;
            this.Southwest = null;
            this.East = null;
            this.West = null;
        }
    }
}
