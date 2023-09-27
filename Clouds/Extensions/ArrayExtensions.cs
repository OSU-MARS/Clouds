namespace Mars.Clouds.Extensions
{
    internal static class ArrayExtensions
    {
        public static int[] CreateSequence(int startInclusive, int endInclusive)
        {
            int[] sequence = new int[endInclusive - startInclusive + 1];
            int value = startInclusive;
            for (int index = 0; index < sequence.Length; ++index, ++value)
            {
                sequence[index] = value;
            }

            return sequence;
        }
    }
}
