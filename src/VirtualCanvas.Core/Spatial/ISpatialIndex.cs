using VirtualCanvas.Core.Geometry;

namespace VirtualCanvas.Core.Spatial;

/// <summary>
/// Interface for spatial index data structures (QuadTree, etc.).
/// Provides fast lookup of <see cref="ISpatialItem"/>s by their rectangular bounds.
/// </summary>
/// <remarks>
/// Phase A minimum contract:
/// <list type="bullet">
///   <item><see cref="Changed"/> — required for VirtualCanvas to react to data changes</item>
///   <item><see cref="GetItemsIntersecting"/> — required for viewport culling / virtualization</item>
///   <item><see cref="Extent"/> — required for QuadTree initialization</item>
/// </list>
/// <see cref="HasItemsIntersecting"/>, <see cref="GetItemsInside"/>, <see cref="HasItemsInside"/>
/// are retained for completeness but not used by the Phase A VirtualCanvas control.
/// </remarks>
public interface ISpatialIndex : IEnumerable<ISpatialItem>
{
    /// <summary>Raised whenever the collection or index is changed.</summary>
    event EventHandler Changed;

    /// <summary>
    /// Outer bounds of all items, or <see cref="VCRect.Empty"/> if none.
    /// <para>
    /// <b>Precondition for implementations:</b> Set <c>Extent</c> to cover the expected
    /// canvas area before inserting items. Inserting into a zero-size or unset extent
    /// degrades query performance (items fall back to root-level storage).
    /// </para>
    /// </summary>
    VCRect Extent { get; }

    /// <summary>
    /// Returns true if any item's bounds intersect <paramref name="bounds"/>.
    /// </summary>
    bool HasItemsIntersecting(VCRect bounds);

    /// <summary>
    /// Returns items whose bounds intersect <paramref name="bounds"/>,
    /// in <b>descending</b> <see cref="ISpatialItem.Priority"/> order
    /// (higher numeric value first).
    /// </summary>
    IEnumerable<ISpatialItem> GetItemsIntersecting(VCRect bounds);

    /// <summary>
    /// Returns true if any item's bounds are fully contained within <paramref name="bounds"/>.
    /// <para>
    /// <b>Traversal limitation:</b> The internal quadrant traversal only recurses into
    /// a child quadrant when the child's area fully contains <paramref name="bounds"/>.
    /// For queries smaller than the leaf quadrants holding the target items,
    /// this method may return <c>false</c> even when items exist inside the bounds.
    /// Use <see cref="GetItemsInside"/> (which uses <c>GetItemsIntersecting</c> + filter)
    /// for guaranteed correctness. This method is most reliable when the query covers
    /// a large portion of the canvas (e.g., full viewport).
    /// </para>
    /// </summary>
    bool HasItemsInside(VCRect bounds);

    /// <summary>
    /// Returns items whose bounds are fully contained within <paramref name="bounds"/>,
    /// in <b>descending</b> <see cref="ISpatialItem.Priority"/> order.
    /// Uses intersection traversal + containment filter — correct for all query sizes.
    /// </summary>
    IEnumerable<ISpatialItem> GetItemsInside(VCRect bounds);
}
