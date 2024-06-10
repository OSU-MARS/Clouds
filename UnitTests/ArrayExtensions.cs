using System;
using System.Text;

namespace Mars.Clouds.UnitTests
{
    internal static class ArrayExtensions
    {
        public static int[] CreateSequence(int length)
        {
            int[] array = new int[length];
            for (int index = 0; index < length; ++index)
            {
                array[index] = index;
            }

            return array;
        }

        public static string GetDeclaration<TElement>(this TElement[,] array)
        {
            StringBuilder arrayAsString = new("[ ");
            int arraySizeX = array.GetLength(0);
            int arraySizeY = array.GetLength(1);
            for (int yIndex = 0; yIndex < arraySizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < arraySizeX; ++xIndex)
                {
                    arrayAsString.Append(array[xIndex, yIndex] + ", ");
                }

                arrayAsString.Append("//" + yIndex + Environment.NewLine + "  ");
            }

            arrayAsString.Append(" ];");
            return arrayAsString.ToString();
        }

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
