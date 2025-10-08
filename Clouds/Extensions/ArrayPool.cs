using System;
using System.Collections.Generic;

namespace Mars.Clouds.Extensions
{
    public class ArrayPool<T>
    {
        private readonly Queue<T[]> objects;

        public int ArrayLength { get; private init; }

        public ArrayPool(int arrayLength)
        {
            this.objects = [];

            this.ArrayLength = arrayLength;
        }

        public void Clear()
        {
            this.objects.Clear();
        }

        public void Return(T[] array)
        {
            if (array.Length != this.ArrayLength)
            {
                throw new ArgumentOutOfRangeException(nameof(array), $"Array's length of {array.Length} elements does not match pool's length of {this.ArrayLength} elements.");
            }

            this.objects.Enqueue(array);
        }

        public T[] TryGetOrAllocateUninitialized()
        {
            if (this.objects.TryDequeue(out T[]? array) == false)
            {
                array = GC.AllocateUninitializedArray<T>(this.ArrayLength, pinned: false);
            }

            return array;
        }
    }
}
