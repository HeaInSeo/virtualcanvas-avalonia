// Ported from VirtualCanvas-ref/src/QuadTree/PriorityQuadTree.QuadNode.cs
// Original: (c) Microsoft Corporation. MIT License.
// Changes: System.Windows.Rect → VCRect, Intersects() → IntersectsWith(), Contains() unchanged.

using VirtualCanvas.Core.Geometry;

namespace VirtualCanvas.Core.Spatial;

public partial class PriorityQuadTree<T>
{
    private class QuadNode
    {
        private VCRect _bounds;
        private QuadNode? _next; // circular linked list
        private T _node;
        private double _priority;

        public QuadNode(T node, VCRect bounds, double priority)
        {
            _node = node;
            _bounds = bounds;
            _priority = priority;
        }

        public T Node
        {
            get => _node;
            set => _node = value;
        }

        public VCRect Bounds => _bounds;
        public double Priority => _priority;

        public QuadNode Next
        {
            get => _next!;
            set => _next = value;
        }

        /// <summary>
        /// Inserts this QuadNode into an existing circular linked list sorted by priority (descending).
        /// Returns the new tail of the list.
        /// </summary>
        public QuadNode InsertInto(QuadNode? tail)
        {
            if (tail == null)
            {
                Next = this;
                return this;
            }

            if (Priority < tail.Priority)
            {
                Next = tail.Next;
                tail.Next = this;
                return this;
            }
            else
            {
                QuadNode x;
                for (x = tail; x.Next != tail && Priority < x.Next.Priority; x = x.Next) { }
                Next = x.Next;
                x.Next = this;
                return tail;
            }
        }

        /// <summary>
        /// Walk the circular linked list and yield nodes that intersect <paramref name="bounds"/>,
        /// along with the priority of the next node.
        /// </summary>
        public IEnumerable<Tuple<QuadNode, double>> GetIntersectingNodes(VCRect bounds)
        {
            QuadNode n = this;
            do
            {
                n = n.Next;
                if (bounds.IntersectsWith(n.Bounds) || bounds == InfiniteBounds)
                    yield return Tuple.Create(n, n != this ? n.Next.Priority : double.NaN);
            }
            while (n != this);
        }

        public bool HasIntersectingNodes(VCRect bounds)
        {
            QuadNode n = this;
            do
            {
                n = n.Next;
                if (bounds.IntersectsWith(n.Bounds) || bounds == InfiniteBounds)
                    return true;
            }
            while (n != this);
            return false;
        }

        public bool HasNodesInside(VCRect bounds)
        {
            QuadNode n = this;
            do
            {
                n = n.Next;
                if (bounds.Contains(n.Bounds) || bounds == InfiniteBounds)
                    return true;
            }
            while (n != this);
            return false;
        }
    }
}
