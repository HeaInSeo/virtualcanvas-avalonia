// Ported from VirtualCanvas-ref/src/QuadTree/PriorityQuadTree.Quadrant.cs
// Original: (c) Microsoft Corporation. MIT License.
// Changes:
//   - System.Windows.Rect → VCRect
//   - new Rect(x,y,w,h) → new VCRect(x,y,w,h)
//   - .Intersects(bounds) → .IntersectsWith(bounds)
//   - .Contains(bounds) → .Contains(bounds) (no name change)
//   - bounds == InfiniteBounds check preserved

using System.Collections;
using VirtualCanvas.Core.Geometry;

namespace VirtualCanvas.Core.Spatial;

public partial class PriorityQuadTree<T>
{
    /// <summary>
    /// Represents a quadrant in the tree.
    /// Objects that overlap sub-quadrant boundaries are stored in this quadrant's node list.
    /// Objects fully contained within a child quadrant are stored recursively there.
    /// </summary>
    private class Quadrant : IEnumerable<QuadNode>
    {
        private VCRect _quadrantBounds;
        private double _maxDescendantPriority = double.NegativeInfinity;
        private int _count;
        private QuadNode? _nodes; // circular linked list of nodes at this level

        private Quadrant? _topLeft;
        private Quadrant? _topRight;
        private Quadrant? _bottomLeft;
        private Quadrant? _bottomRight;

        public Quadrant(VCRect bounds)
        {
            _quadrantBounds = bounds;
        }

        internal Quadrant Insert(T node, VCRect bounds, double priority, int depth)
        {
            _maxDescendantPriority = Math.Max(_maxDescendantPriority, priority);
            _count++;

            Quadrant? child = null;

            if (depth <= MaxTreeDepth && (bounds.Width > 0 || bounds.Height > 0))
            {
                double w = _quadrantBounds.Width / 2;
                double h = _quadrantBounds.Height / 2;

                var topLeft = new VCRect(_quadrantBounds.Left, _quadrantBounds.Top, w, h);
                var topRight = new VCRect(_quadrantBounds.Left + w, _quadrantBounds.Top, w, h);
                var bottomLeft = new VCRect(_quadrantBounds.Left, _quadrantBounds.Top + h, w, h);
                var bottomRight = new VCRect(_quadrantBounds.Left + w, _quadrantBounds.Top + h, w, h);

                if (topLeft.Contains(bounds) || bounds == InfiniteBounds)
                {
                    _topLeft ??= new Quadrant(topLeft);
                    child = _topLeft;
                }
                else if (topRight.Contains(bounds) || bounds == InfiniteBounds)
                {
                    _topRight ??= new Quadrant(topRight);
                    child = _topRight;
                }
                else if (bottomLeft.Contains(bounds) || bounds == InfiniteBounds)
                {
                    _bottomLeft ??= new Quadrant(bottomLeft);
                    child = _bottomLeft;
                }
                else if (bottomRight.Contains(bounds) || bounds == InfiniteBounds)
                {
                    _bottomRight ??= new Quadrant(bottomRight);
                    child = _bottomRight;
                }
            }

            if (child != null)
                return child.Insert(node, bounds, priority, depth + 1);

            var n = new QuadNode(node, bounds, priority);
            _nodes = n.InsertInto(_nodes);
            return this;
        }

        internal bool Remove(T node, VCRect bounds)
        {
            bool removed = false;

            if (RemoveNode(node))
            {
                removed = true;
            }
            else
            {
                double w = _quadrantBounds.Width / 2;
                double h = _quadrantBounds.Height / 2;

                var topLeft = new VCRect(_quadrantBounds.Left, _quadrantBounds.Top, w, h);
                var topRight = new VCRect(_quadrantBounds.Left + w, _quadrantBounds.Top, w, h);
                var bottomLeft = new VCRect(_quadrantBounds.Left, _quadrantBounds.Top + h, w, h);
                var bottomRight = new VCRect(_quadrantBounds.Left + w, _quadrantBounds.Top + h, w, h);

                if (_topLeft != null && (topLeft.IntersectsWith(bounds) || bounds == InfiniteBounds) && _topLeft.Remove(node, bounds))
                {
                    if (_topLeft._count == 0) _topLeft = null;
                    removed = true;
                }
                else if (_topRight != null && (topRight.IntersectsWith(bounds) || bounds == InfiniteBounds) && _topRight.Remove(node, bounds))
                {
                    if (_topRight._count == 0) _topRight = null;
                    removed = true;
                }
                else if (_bottomLeft != null && (bottomLeft.IntersectsWith(bounds) || bounds == InfiniteBounds) && _bottomLeft.Remove(node, bounds))
                {
                    if (_bottomLeft._count == 0) _bottomLeft = null;
                    removed = true;
                }
                else if (_bottomRight != null && (bottomRight.IntersectsWith(bounds) || bounds == InfiniteBounds) && _bottomRight.Remove(node, bounds))
                {
                    if (_bottomRight._count == 0) _bottomRight = null;
                    removed = true;
                }
            }

            if (removed)
            {
                _count--;
                _maxDescendantPriority = CalculateQuadrantPriority();
                return true;
            }

            return false;
        }

        internal IEnumerable<Tuple<QuadNode, double>> GetIntersectingNodes(VCRect bounds)
        {
            double w = _quadrantBounds.Width / 2;
            double h = _quadrantBounds.Height / 2;

            var topLeft = new VCRect(_quadrantBounds.Left, _quadrantBounds.Top, w, h);
            var topRight = new VCRect(_quadrantBounds.Left + w, _quadrantBounds.Top, w, h);
            var bottomLeft = new VCRect(_quadrantBounds.Left, _quadrantBounds.Top + h, w, h);
            var bottomRight = new VCRect(_quadrantBounds.Left + w, _quadrantBounds.Top + h, w, h);

            var queue = new PriorityQueue<IEnumerator<Tuple<QuadNode, double>>, double>(true);

            if (_nodes != null)
                queue.Enqueue(_nodes.GetIntersectingNodes(bounds).GetEnumerator(), _nodes.Next.Priority);

            if (_topLeft != null && (topLeft.IntersectsWith(bounds) || bounds == InfiniteBounds))
                queue.Enqueue(_topLeft.GetIntersectingNodes(bounds).GetEnumerator(), _topLeft._maxDescendantPriority);

            if (_topRight != null && (topRight.IntersectsWith(bounds) || bounds == InfiniteBounds))
                queue.Enqueue(_topRight.GetIntersectingNodes(bounds).GetEnumerator(), _topRight._maxDescendantPriority);

            if (_bottomLeft != null && (bottomLeft.IntersectsWith(bounds) || bounds == InfiniteBounds))
                queue.Enqueue(_bottomLeft.GetIntersectingNodes(bounds).GetEnumerator(), _bottomLeft._maxDescendantPriority);

            if (_bottomRight != null && (bottomRight.IntersectsWith(bounds) || bounds == InfiniteBounds))
                queue.Enqueue(_bottomRight.GetIntersectingNodes(bounds).GetEnumerator(), _bottomRight._maxDescendantPriority);

            while (queue.Count > 0)
            {
                var enumerator = queue.Dequeue().Key;
                if (enumerator.MoveNext())
                {
                    var current = enumerator.Current;
                    var qnode = current.Item1;
                    var potential = current.Item2;

                    var newPotential = queue.Count > 0
                        ? !double.IsNaN(potential) ? Math.Max(potential, queue.Peek().Value) : queue.Peek().Value
                        : potential;

                    if (newPotential > qnode.Priority)
                    {
                        // Defer this node — something with higher priority may still come
                        var store = Enumerable.Repeat(Tuple.Create(qnode, double.NaN), 1).GetEnumerator();
                        queue.Enqueue(store, qnode.Priority);
                    }
                    else
                    {
                        yield return Tuple.Create(qnode, newPotential);
                    }

                    if (!double.IsNaN(potential))
                        queue.Enqueue(enumerator, potential);
                }
            }
        }

        internal bool HasNodesInside(VCRect bounds)
        {
            double w = _quadrantBounds.Width / 2;
            double h = _quadrantBounds.Height / 2;

            var topLeft = new VCRect(_quadrantBounds.Left, _quadrantBounds.Top, w, h);
            var topRight = new VCRect(_quadrantBounds.Left + w, _quadrantBounds.Top, w, h);
            var bottomLeft = new VCRect(_quadrantBounds.Left, _quadrantBounds.Top + h, w, h);
            var bottomRight = new VCRect(_quadrantBounds.Left + w, _quadrantBounds.Top + h, w, h);

            if (_nodes != null && _nodes.HasNodesInside(bounds))
                return true;
            if (_topLeft != null && (topLeft.Contains(bounds) || bounds == InfiniteBounds) && _topLeft.HasNodesInside(bounds))
                return true;
            if (_topRight != null && (topRight.Contains(bounds) || bounds == InfiniteBounds) && _topRight.HasNodesInside(bounds))
                return true;
            if (_bottomLeft != null && (bottomLeft.Contains(bounds) || bounds == InfiniteBounds) && _bottomLeft.HasNodesInside(bounds))
                return true;
            if (_bottomRight != null && (bottomRight.Contains(bounds) || bounds == InfiniteBounds) && _bottomRight.HasNodesInside(bounds))
                return true;
            return false;
        }

        internal bool HasIntersectingNodes(VCRect bounds)
        {
            double w = _quadrantBounds.Width / 2;
            double h = _quadrantBounds.Height / 2;

            var topLeft = new VCRect(_quadrantBounds.Left, _quadrantBounds.Top, w, h);
            var topRight = new VCRect(_quadrantBounds.Left + w, _quadrantBounds.Top, w, h);
            var bottomLeft = new VCRect(_quadrantBounds.Left, _quadrantBounds.Top + h, w, h);
            var bottomRight = new VCRect(_quadrantBounds.Left + w, _quadrantBounds.Top + h, w, h);

            if (_nodes != null && _nodes.HasIntersectingNodes(bounds))
                return true;
            if (_topLeft != null && (topLeft.IntersectsWith(bounds) || bounds == InfiniteBounds) && _topLeft.HasIntersectingNodes(bounds))
                return true;
            if (_topRight != null && (topRight.IntersectsWith(bounds) || bounds == InfiniteBounds) && _topRight.HasIntersectingNodes(bounds))
                return true;
            if (_bottomLeft != null && (bottomLeft.IntersectsWith(bounds) || bounds == InfiniteBounds) && _bottomLeft.HasIntersectingNodes(bounds))
                return true;
            if (_bottomRight != null && (bottomRight.IntersectsWith(bounds) || bounds == InfiniteBounds) && _bottomRight.HasIntersectingNodes(bounds))
                return true;
            return false;
        }

        private bool RemoveNode(T node)
        {
            if (_nodes == null) return false;

            QuadNode p = _nodes;
            while (!object.Equals(p.Next.Node, node) && p.Next != _nodes)
                p = p.Next;

            if (!object.Equals(p.Next.Node, node))
                return false;

            QuadNode n = p.Next;
            if (p == n)
            {
                _nodes = null; // list goes empty
            }
            else
            {
                if (_nodes == n) _nodes = p;
                p.Next = n.Next;
            }
            return true;
        }

        private double CalculateQuadrantPriority()
        {
            double priority = double.NegativeInfinity;
            if (_nodes != null) priority = _nodes.Next.Priority;
            if (_topLeft != null) priority = Math.Max(priority, _topLeft._maxDescendantPriority);
            if (_topRight != null) priority = Math.Max(priority, _topRight._maxDescendantPriority);
            if (_bottomLeft != null) priority = Math.Max(priority, _bottomLeft._maxDescendantPriority);
            if (_bottomRight != null) priority = Math.Max(priority, _bottomRight._maxDescendantPriority);
            return priority;
        }

        public IEnumerator<QuadNode> GetEnumerator()
        {
            var queue = new Queue<Quadrant>();
            queue.Enqueue(this);

            while (queue.Count > 0)
            {
                var quadrant = queue.Dequeue();

                if (quadrant._nodes != null)
                {
                    var start = quadrant._nodes;
                    var n = quadrant._nodes;
                    do
                    {
                        n = n.Next;
                        yield return n;
                    }
                    while (n != start);
                }

                if (quadrant._topLeft != null) queue.Enqueue(quadrant._topLeft);
                if (quadrant._topRight != null) queue.Enqueue(quadrant._topRight);
                if (quadrant._bottomLeft != null) queue.Enqueue(quadrant._bottomLeft);
                if (quadrant._bottomRight != null) queue.Enqueue(quadrant._bottomRight);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
