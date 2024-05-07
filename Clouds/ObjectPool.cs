using System.Collections.Generic;

namespace Mars.Clouds
{
    public class ObjectPool<T> where T : class, new()
    {
        private readonly Queue<T> objects;

        public ObjectPool()
        {
            this.objects = [];
        }

        public void Clear()
        {
            this.objects.Clear();
        }

        public void GetThreadSafe(List<T> items, int count)
        {
            lock (this.objects)
            {
                for (int index = 0; index < count; ++index)
                {
                    if (this.objects.TryDequeue(out T? item) == false)
                    {
                        item = new();
                    }
                    items.Add(item);
                }
            }
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
    }
}
