using System;
using System.Collections.Generic;

namespace LtvDiagnostics
{
    public class MaxHeap<T> where T : IComparable<T>
    {
        private readonly List<T> items = new List<T>();

        public int Count => items.Count;

        public void Insert(T item)
        {
            items.Add(item);
            BubbleUp(items.Count - 1);
        }

        public T Peek()
        {
            if (items.Count == 0)
            {
                throw new InvalidOperationException("Heap is empty.");
            }

            return items[0];
        }

        public T ExtractMax()
        {
            if (items.Count == 0)
            {
                throw new InvalidOperationException("Heap is empty.");
            }

            T max = items[0];
            int lastIndex = items.Count - 1;
            items[0] = items[lastIndex];
            items.RemoveAt(lastIndex);

            if (items.Count > 0)
            {
                BubbleDown(0);
            }

            return max;
        }

        public void Clear()
        {
            items.Clear();
        }

        private void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (items[index].CompareTo(items[parent]) <= 0)
                {
                    break;
                }

                Swap(index, parent);
                index = parent;
            }
        }

        private void BubbleDown(int index)
        {
            int count = items.Count;
            while (true)
            {
                int left = 2 * index + 1;
                int right = 2 * index + 2;
                int largest = index;

                if (left < count && items[left].CompareTo(items[largest]) > 0)
                {
                    largest = left;
                }

                if (right < count && items[right].CompareTo(items[largest]) > 0)
                {
                    largest = right;
                }

                if (largest == index)
                {
                    break;
                }

                Swap(index, largest);
                index = largest;
            }
        }

        private void Swap(int a, int b)
        {
            T temp = items[a];
            items[a] = items[b];
            items[b] = temp;
        }
    }
}
