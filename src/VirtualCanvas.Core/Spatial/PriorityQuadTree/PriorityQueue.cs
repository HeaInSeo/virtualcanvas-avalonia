// Ported from VirtualCanvas-ref/src/QuadTree/PriorityQueue.cs
// Original: (c) Microsoft Corporation. MIT License.
// Changes: namespace only.

using System.Diagnostics.CodeAnalysis;

namespace VirtualCanvas.Core.Spatial;

/// <summary>
/// A max-heap priority queue. Supports re-enqueueing the same item with updated priority.
/// Used internally by PriorityQuadTree for lazy prioritized enumeration.
/// </summary>
[SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
internal class PriorityQueue<T, TPriority> where T : notnull
{
    private readonly List<KeyValuePair<T, TPriority>> _heap = new List<KeyValuePair<T, TPriority>>();
    private readonly Dictionary<T, int> _indexes = new Dictionary<T, int>();
    private readonly IComparer<TPriority> _comparer;
    private readonly bool _invert;

    public PriorityQueue(bool invert)
        : this(Comparer<TPriority>.Default, invert) { }

    public PriorityQueue(IComparer<TPriority> comparer, bool invert)
    {
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        _invert = invert;
        _heap.Add(default!); // 1-based indexing sentinel
    }

    public int Count => _heap.Count - 1;

    public void Enqueue(T item, TPriority priority)
    {
        var entry = new KeyValuePair<T, TPriority>(item, priority);
        _heap.Add(entry);
        MoveUp(entry, Count);
    }

    public KeyValuePair<T, TPriority> Dequeue()
    {
        int bound = Count;
        if (bound < 1)
            throw new InvalidOperationException("Queue is empty.");

        var head = _heap[1];
        var tail = _heap[bound];
        _heap.RemoveAt(bound);

        if (bound > 1)
            MoveDown(tail, 1);

        _indexes.Remove(head.Key);
        return head;
    }

    public KeyValuePair<T, TPriority> Peek()
    {
        if (Count < 1)
            throw new InvalidOperationException("Queue is empty.");
        return _heap[1];
    }

    private void MoveUp(KeyValuePair<T, TPriority> element, int index)
    {
        while (index > 1)
        {
            int parent = index >> 1;
            if (IsPrior(_heap[parent], element))
                break;
            _heap[index] = _heap[parent];
            _indexes[_heap[parent].Key] = index;
            index = parent;
        }
        _heap[index] = element;
        _indexes[element.Key] = index;
    }

    private void MoveDown(KeyValuePair<T, TPriority> element, int index)
    {
        int count = _heap.Count;
        while (index << 1 < count)
        {
            int child = index << 1;
            int sibling = child | 1;
            if (sibling < count && IsPrior(_heap[sibling], _heap[child]))
                child = sibling;
            if (IsPrior(element, _heap[child]))
                break;
            _heap[index] = _heap[child];
            _indexes[_heap[child].Key] = index;
            index = child;
        }
        _heap[index] = element;
        _indexes[element.Key] = index;
    }

    private bool IsPrior(KeyValuePair<T, TPriority> a, KeyValuePair<T, TPriority> b)
    {
        int order = _comparer.Compare(a.Value, b.Value);
        if (_invert) order = ~order;
        return order < 0;
    }
}
