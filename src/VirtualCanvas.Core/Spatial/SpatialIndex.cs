using System.Collections;
using VirtualCanvas.Core.Geometry;

namespace VirtualCanvas.Core.Spatial;

/// <summary>
/// Concrete <see cref="ISpatialIndex"/> backed by a <see cref="PriorityQuadTree{T}"/>.
/// <para>
/// Set <see cref="Extent"/> before inserting items for optimal spatial query performance.
/// Individual <see cref="Insert"/> calls do not raise <see cref="Changed"/>;
/// call <see cref="RaiseChanged"/> (or <see cref="Clear"/>) when the batch is complete.
/// </para>
/// </summary>
public sealed class SpatialIndex : ISpatialIndex
{
    private readonly PriorityQuadTree<ISpatialItem> _tree = new();

    public event EventHandler? Changed;

    /// <summary>
    /// The bounding rect used to partition the quad tree.
    /// Set before inserting items; see <see cref="PriorityQuadTree{T}.Extent"/>.
    /// </summary>
    public VCRect Extent
    {
        get => _tree.Extent;
        set => _tree.Extent = value;
    }

    /// <summary>Inserts an item using its <see cref="ISpatialItem.Bounds"/> and <see cref="ISpatialItem.Priority"/>.</summary>
    public void Insert(ISpatialItem item)
        => _tree.Insert(item, item.Bounds, item.Priority);

    /// <summary>
    /// Removes <paramref name="item"/> from the index.
    /// Searches the full tree extent so callers do not need to track the original bounds.
    /// Call <see cref="RaiseChanged"/> after the batch is complete to notify the canvas.
    /// </summary>
    /// <returns><c>true</c> if the item was found and removed; <c>false</c> otherwise.</returns>
    public bool Remove(ISpatialItem item) => _tree.Remove(item);

    /// <summary>Clears all items and raises <see cref="Changed"/>.</summary>
    public void Clear()
    {
        _tree.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises <see cref="Changed"/> to notify the canvas of a data update.</summary>
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public bool HasItemsIntersecting(VCRect bounds) => _tree.HasItemsIntersecting(bounds);
    public IEnumerable<ISpatialItem> GetItemsIntersecting(VCRect bounds) => _tree.GetItemsIntersecting(bounds);
    public bool HasItemsInside(VCRect bounds) => _tree.HasItemsInside(bounds);
    public IEnumerable<ISpatialItem> GetItemsInside(VCRect bounds) => _tree.GetItemsInside(bounds);

    public IEnumerator<ISpatialItem> GetEnumerator() => _tree.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
