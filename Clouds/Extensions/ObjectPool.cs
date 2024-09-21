using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Extensions
{
    public class ObjectPool<T> where T : class
    {
        // could also use ConcurrentQueue<T>
        // From Microsoft's documentation ConcurrentQueue<T> is probably slightly faster than lock { Queue<T> } for occasional enqueuing
        // or dequeuing and almost certainly slower when adding or removing objects in batches.
        private readonly Queue<T> objects;

        public ObjectPool()
        {
            this.objects = [];
        }

        public int Count
        {
            get { return this.objects.Count; }
        }

        public void Clear()
        {
            this.objects.Clear();
        }

        public void Return(T item) 
        {
            this.objects.Enqueue(item);
        }

        public bool TryGet([NotNullWhen(true)] out T? item)
        {
            return this.objects.TryDequeue(out item);
        }
    }
}
