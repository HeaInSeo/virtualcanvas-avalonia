using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Avalonia.DevApp.Viewer;

/// <summary>
/// Viewer PoC spike: models the hand-off shape from a hypothetical DagEdit projection.
/// <para>
/// This type does NOT reference any DagEdit assembly. The shape (Id, Label, Bounds, …)
/// is deliberately minimal — enough to verify that VCA can realize projected items.
/// The full DagEdit node type is intentionally absent; only the projection seam matters.
/// </para>
/// </summary>
internal sealed class ProjectedNodeItem : ISpatialItem
{
    public required string Id    { get; init; }
    public required string Label { get; init; }

    // Mutable: remove→update→reinsert pattern for in-place moves.
    public VCRect  Bounds   { get; set; }
    public double  Priority { get; init; }
    public int     ZIndex   { get; init; }

    // Mutable: IsVisible toggle does NOT require remove/reinsert.
    // VCA pre-filters IsVisible=false items; a plain RaiseChanged() suffices.
    public bool    IsVisible { get; set; } = true;
}
