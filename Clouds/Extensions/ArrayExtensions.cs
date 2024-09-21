using System;

namespace Mars.Clouds.Extensions
{
    public static class ArrayExtensions
    {
        public static T[,] Copy<T>(this T[,] array)
        {
            T[,] clone = new T[array.GetLength(0), array.GetLength(1)];
            Array.Copy(array, clone, array.Length);
            return clone;
        }

        public static T[] Extend<T>(this T[] array, int newCapacity)
        {
            if (newCapacity <= array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(newCapacity), "Requested length of " + newCapacity + " is the same or less than the array's current length of " + array.Length + ".");
            }

            T[] extendedArray = new T[newCapacity];
            Array.Copy(array, extendedArray, array.Length);
            return extendedArray;
        }

        public static T[,] Extend<T>(this T[,] array, int newRowCount)
        {
            int currentRows = array.GetLength(0);
            if (newRowCount <= currentRows)
            {
                throw new ArgumentOutOfRangeException(nameof(newRowCount), "Requested length of " + newRowCount + " rows is the same or less than the array's current length of " + currentRows + " rows.");
            }

            T[,] extendedArray = new T[newRowCount, array.GetLength(1)];
            Array.Copy(array, extendedArray, array.Length); // can use Array.Copy() here since number of columns does not change
            return extendedArray;
        }
    }
}
