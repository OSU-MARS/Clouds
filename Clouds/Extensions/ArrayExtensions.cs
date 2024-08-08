using System;

namespace Mars.Clouds.Extensions
{
    public static class ArrayExtensions
    {
        public static T[,] DeepClone<T>(this T[,] array)
        {
            T[,] clone = new T[array.GetLength(0), array.GetLength(1)];
            Array.Copy(array, clone, array.Length);
            return clone;
        }
    }
}
