// Ported from VirtualCanvas-ref/src/QuadTree/PriorityQuadTree.cs
// Original: (c) Microsoft Corporation. MIT License.
// Changes:
//   - System.Windows.Rect → VCRect
//   - Rect.Empty → VCRect.Empty
//   - WpfHelper.IsDefined() extension → VCRect.IsDefined property
//   - WpfHelper.Intersects() extension → VCRect.IntersectsWith()
//   - Namespace: VirtualCanvasDemo.QuadTree → VirtualCanvas.Core.Spatial

using System.Collections;
using VirtualCanvas.Core.Geometry;

namespace VirtualCanvas.Core.Spatial;

/// <summary>
/// Efficiently stores and lazily retrieves arbitrarily sized and positioned objects
/// in a prioritized order using a quad-tree data structure.
/// Items with <b>higher</b> numeric priority values are returned first.
/// </summary>
/// <remarks>
/// Original class written by Chris Lovett.
/// Prioritization and lazy enumeration added by Kael Rowan.
/// Ported to VirtualCanvas.Core (Avalonia) — WPF dependencies removed.
/// <para>
/// <b>Priority ordering:</b> The WPF reference XML comment claimed "lower values first" —
/// that is incorrect. The QuadNode circular linked list stores nodes with the highest
/// numeric priority at the head (tail.Next), so higher values are returned first.
/// The PriorityQueue used in quadrant traversal is also a max-heap (invert=true).
/// </para>
/// <para>
/// <b>Recommended usage:</b> Set <see cref="Extent"/> before inserting items.
/// Inserting without a proper Extent causes all items to accumulate in the root node,
/// degrading query performance to O(n).
/// </para>
/// </remarks>
public partial class PriorityQuadTree<T> : IEnumerable<T>
{
    private VCRect _publicExtent = VCRect.Empty;
    private VCRect _realExtent = new VCRect(0.0, 0.0, 0.0, 0.0);
    private Quadrant? _root;
    private readonly HashSet<T> _emptyItems = new HashSet<T>();
    private readonly VCRect _emptyBounds = new VCRect(0, 0, 0, 0);

    // MaxTreeDepth prevents stack overflow when item bounds are tiny relative to Extent.
    // With depth 50, item bounds can be 2^-50 times the extent before tree stops growing.
    private const int MaxTreeDepth = 50;

    /// <summary>
    /// The overall quad-tree indexing bounds.
    /// Changing this is expensive (triggers re-index of all items).
    /// </summary>
    /// <remarks>
    /// Set this before inserting items. If not set (or set to <see cref="VCRect.Empty"/>),
    /// <c>_realExtent</c> remains <c>(0,0,0,0)</c> and inserted items fall back to
    /// root-level storage, degrading spatial queries to O(n) linear scans.
    /// Shrinking to less than half the current area also triggers a full re-index.
    /// </remarks>
    public VCRect Extent
    {
        get => _publicExtent;
        set
        {
            if (!(value.Top >= double.MinValue &&
                  value.Top <= double.MaxValue &&
                  value.Left >= double.MinValue &&
                  value.Left <= double.MaxValue &&
                  value.Width <= double.MaxValue &&
                  value.Height <= double.MaxValue) &&
                  value != VCRect.Empty)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _publicExtent = value;

            // Only re-index if new extent falls outside current real extent,
            // or has shrunk significantly.
            if (value != VCRect.Empty &&
                (!_realExtent.Contains(value) ||
                 (value.Width * value.Height * 2) < (_realExtent.Width * _realExtent.Height)))
            {
                _realExtent = value;
                ReIndex();
            }
        }
    }

    /// <summary>
    /// Insert an item with given bounds and priority.
    /// </summary>
    /// <remarks>
    /// Items with exactly <c>(0,0,0,0)</c> bounds are stored in a separate flat list
    /// (<c>emptyItems</c>) and are always returned by infinite-bounds queries.
    /// NaN priority is normalized to <see cref="double.NegativeInfinity"/> (lowest).
    /// <para>
    /// <b>Precondition:</b> <paramref name="bounds"/> must satisfy <see cref="VCRect.IsDefined"/>
    /// (no NaN, not <see cref="VCRect.Empty"/>). Throws <see cref="ArgumentOutOfRangeException"/>
    /// otherwise.
    /// </para>
    /// </remarks>
    public void Insert(T item, VCRect bounds, double priority)
    {
        if (!bounds.IsDefined)
            throw new ArgumentOutOfRangeException(nameof(bounds));

        if (bounds == _emptyBounds)
        {
            _emptyItems.Add(item);
            return;
        }

        _root ??= new Quadrant(_realExtent);

        if (double.IsNaN(priority))
            priority = double.NegativeInfinity;

        _root.Insert(item, bounds, priority, 1);
    }

    /// <summary>
    /// Returns true if any item's bounds are fully contained within <paramref name="bounds"/>.
    /// </summary>
    /// <remarks>
    /// <b>Traversal limitation (inherited from WPF reference):</b>
    /// The quadrant traversal only recurses into a child when that child's area
    /// fully contains <paramref name="bounds"/>. For queries smaller than the leaf
    /// quadrants holding the items, this may return <c>false</c> incorrectly.
    /// <para>
    /// This works reliably when <paramref name="bounds"/> is large relative to the
    /// canvas (e.g., a full viewport query covering most of the extent).
    /// For correctness in all cases, iterate <see cref="GetItemsInside"/> instead.
    /// </para>
    /// </remarks>
    public bool HasItemsInside(VCRect bounds)
    {
        if (!bounds.IsDefined)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        if (bounds == _emptyBounds && _emptyItems.Count > 0)
            return true;
        return _root?.HasNodesInside(bounds) ?? false;
    }

    /// <summary>
    /// Returns items whose bounds are fully contained within <paramref name="bounds"/>,
    /// in descending priority order (higher numeric value first).
    /// Uses intersection traversal + containment filter — correct for all query sizes.
    /// </summary>
    public IEnumerable<T> GetItemsInside(VCRect bounds)
    {
        if (!bounds.IsDefined)
            throw new ArgumentOutOfRangeException(nameof(bounds));

        if ((bounds.Contains(_emptyBounds) || bounds == InfiniteBounds) && _emptyItems.Count > 0)
        {
            foreach (T item in _emptyItems)
                yield return item;
        }

        if (_root != null)
        {
            foreach (var node in _root.GetIntersectingNodes(bounds))
            {
                if (bounds.Contains(node.Item1.Bounds) || bounds == InfiniteBounds)
                    yield return node.Item1.Node;
            }
        }
    }

    /// <summary>Gets whether any items intersect the given bounds.</summary>
    public bool HasItemsIntersecting(VCRect bounds)
    {
        if (!bounds.IsDefined)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        if ((bounds.IntersectsWith(_emptyBounds) || bounds == InfiniteBounds) && _emptyItems.Count > 0)
            return true;
        return _root?.HasIntersectingNodes(bounds) ?? false;
    }

    /// <summary>
    /// Returns items whose bounds intersect <paramref name="bounds"/>,
    /// in descending priority order (higher numeric value first).
    /// </summary>
    public IEnumerable<T> GetItemsIntersecting(VCRect bounds)
    {
        if (!bounds.IsDefined)
            throw new ArgumentOutOfRangeException(nameof(bounds));

        if ((bounds.IntersectsWith(_emptyBounds) || bounds == InfiniteBounds) && _emptyItems.Count > 0)
        {
            foreach (T item in _emptyItems)
                yield return item;
        }

        if (_root != null)
        {
            foreach (var node in _root.GetIntersectingNodes(bounds))
                yield return node.Item1.Node;
        }
    }

    /// <summary>Removes the first instance of the given item (full tree search).</summary>
    public virtual bool Remove(T item) => Remove(item, InfiniteBounds);

    /// <summary>Removes the first instance of the given item within the search bounds.</summary>
    public bool Remove(T item, VCRect bounds)
    {
        if (!bounds.IsDefined)
            throw new ArgumentOutOfRangeException(nameof(bounds));

        if (_emptyItems.Contains(item))
            return _emptyItems.Remove(item);

        return _root?.Remove(item, bounds) ?? false;
    }

    /// <summary>Removes all nodes from the tree.</summary>
    public virtual void Clear()
    {
        _root = null;
        _emptyItems.Clear();
    }

    /// <summary>
    /// Covers the entire plane. Guaranteed to contain every item in the quad tree.
    /// </summary>
    internal static readonly VCRect InfiniteBounds = VCRect.Infinite;

    private void ReIndex()
    {
        var oldRoot = _root;
        _root = new Quadrant(_realExtent);
        if (oldRoot != null)
        {
            foreach (var node in oldRoot.GetIntersectingNodes(_realExtent))
                Insert(node.Item1.Node, node.Item1.Bounds, node.Item1.Priority);
        }
    }

    /// <summary>Returns all items in the tree in unspecified order.</summary>
    public IEnumerator<T> GetEnumerator()
    {
        foreach (var item in _emptyItems)
            yield return item;

        if (_root != null)
        {
            foreach (var node in _root)
                yield return node.Node;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
