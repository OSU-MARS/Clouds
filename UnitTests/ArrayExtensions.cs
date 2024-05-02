using System;

namespace Mars.Clouds.UnitTests
{
    internal static class ArrayExtensions
    {
        /// <summary>
        /// Randomize order of elements in array.
        /// </summary>
        public static void RandomizeOrder<TElement>(this TElement[] array)
        {
            // Fisher-Yates shuffle
            // https://stackoverflow.com/questions/108819/best-way-to-randomize-an-array-with-net
            for (int destinationIndex = array.Length; destinationIndex > 1; /* decrement in body */)
            {
                int sourceIndex = Random.Shared.Next(destinationIndex--);
                (array[sourceIndex], array[destinationIndex]) = (array[destinationIndex], array[sourceIndex]);
            }
        }
    }
}
