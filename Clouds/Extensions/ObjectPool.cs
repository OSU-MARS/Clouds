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

        public void ReturnThreadSafe(List<T> items)
        {
            lock (this.objects) 
            {
                for (int index = 0; index < items.Count; ++index)
                {
                    this.objects.Enqueue(items[index]);
                }
            }
        }

        public bool TryGet([NotNullWhen(true)] out T? item)
        {
            return this.objects.TryDequeue(out item);
        }

        public bool TryGetThreadSafe([NotNullWhen(true)] out T? item)
        {
            if (this.Count == 0)
            {
                item = null;
                return false;
            }
            lock (this.objects)
            {
                return this.objects.TryDequeue(out item);
            }
        }

        public int TryGetThreadSafe(List<T> items, int count)
        {
            if (this.Count == 0)
            {
                return 0;
            }

            int itemsAdded = 0;
            lock (this.objects)
            {
                int maxObjectCount = Int32.Min(count, this.Count);
                for (int objectCount = 0; objectCount < maxObjectCount; ++objectCount)
                {
                    if (this.objects.TryDequeue(out T? item))
                    {
                        items.Add(item);
                        ++itemsAdded;
                    }
                }
            }

            return itemsAdded;
        }
    }
}
