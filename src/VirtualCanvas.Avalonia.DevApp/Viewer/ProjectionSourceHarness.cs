using VirtualCanvas.Core.Geometry;

namespace VirtualCanvas.Avalonia.DevApp.Viewer;

/// <summary>
/// Minimal projection source harness — simulates the DagEdit-side projection seam.
/// <para>
/// Owns the current set of projected nodes and fires <see cref="ProjectionChanged"/>
/// whenever the set changes. Does NOT know about VCA, ISpatialIndex, or IVisualFactory.
/// The consumer wires <see cref="ProjectionChanged"/> to rebuild the canvas index.
/// </para>
/// <para>
/// Wiring pattern (one line):
/// <code>
///   source.ProjectionChanged += (_, _) => { canvas.Items = Snapshot(source.Nodes); };
/// </code>
/// </para>
/// <para>
/// This is a proof-of-concept harness, not the final DagEdit adapter.
/// Final batching / incremental-diff policy is intentionally left unresolved.
/// </para>
/// </summary>
internal sealed class ProjectionSourceHarness
{
    private readonly List<ProjectedNodeItem> _nodes = new();

    /// <summary>Fires whenever the node set changes (add / remove / move / visibility).</summary>
    public event EventHandler? ProjectionChanged;

    /// <summary>The current projected node set, in insertion order.</summary>
    public IReadOnlyList<ProjectedNodeItem> Nodes => _nodes;

    /// <summary>Last node, or <c>null</c> if the set is empty.</summary>
    public ProjectedNodeItem? Last => _nodes.Count > 0 ? _nodes[^1] : null;

    /// <summary>Returns the node with the given <paramref name="id"/>, or <c>null</c>.</summary>
    public ProjectedNodeItem? FindById(string id) => _nodes.Find(x => x.Id == id);

    // ── Mutations (each fires ProjectionChanged) ──────────────────────────────

    /// <summary>Adds a new node to the projection and notifies.</summary>
    public void Add(ProjectedNodeItem node)
    {
        _nodes.Add(node);
        Notify();
    }

    /// <summary>
    /// Removes the node with <paramref name="id"/> from the projection and notifies.
    /// No-op if the id is not found.
    /// </summary>
    public void Remove(string id)
    {
        if (_nodes.RemoveAll(x => x.Id == id) > 0)
            Notify();
    }

    /// <summary>
    /// Toggles <see cref="ProjectedNodeItem.IsVisible"/> for the node with <paramref name="id"/>.
    /// In-place mutation: no remove/reinsert — VCA's pre-filter handles the rest.
    /// </summary>
    public void SetVisible(string id, bool visible)
    {
        var n = _nodes.Find(x => x.Id == id);
        if (n != null) { n.IsVisible = visible; Notify(); }
    }

    /// <summary>
    /// Updates <see cref="ProjectedNodeItem.Bounds"/> for the node with <paramref name="id"/>.
    /// In-place mutation: the same object reference is preserved so VCA can reuse the Control.
    /// </summary>
    public void Move(string id, VCRect newBounds)
    {
        var n = _nodes.Find(x => x.Id == id);
        if (n != null) { n.Bounds = newBounds; Notify(); }
    }

    private void Notify() => ProjectionChanged?.Invoke(this, EventArgs.Empty);
}
