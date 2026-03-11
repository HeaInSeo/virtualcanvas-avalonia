using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VirtualCanvas.Avalonia.Factories;
using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;
using Xunit;

using VCCanvas = VirtualCanvas.Avalonia.Controls.VirtualCanvas;

namespace VirtualCanvas.Avalonia.Tests;

// ── file-scoped: integration test harness only ────────────────────────────────

/// <summary>
/// Counts factory calls so integration tests can verify Control reuse vs re-creation.
/// </summary>
file sealed class IntegrationCountingFactory : IVisualFactory
{
    public int RealizeCallCount { get; private set; }

    public void BeginRealize() { }
    public void EndRealize()   { }

    public Control? Realize(ISpatialItem item, bool force)
    {
        RealizeCallCount++;
        return item is LifecycleMutableItem p ? new Border { Tag = p.Label } : null;
    }

    public bool Virtualize(Control visual) => true;
}

/// <summary>
/// Mirrors the DagEdit-side "projection adapter" pattern:
/// <list type="bullet">
///   <item>Owns stable <see cref="LifecycleMutableItem"/> references (no new objects per flush).</item>
///   <item>Exposes <see cref="BuildSnapshot"/> — builds a fresh <see cref="SpatialIndex"/>
///         from the current item list, inserting the SAME object references every time.</item>
///   <item>Fires <see cref="ProjectionChanged"/> on every mutation (add / remove / move / hide).</item>
/// </list>
/// <para>
/// The consumer wires it in one line:
/// <code>
///   adapter.ProjectionChanged += (_, _) => canvas.Items = adapter.BuildSnapshot();
/// </code>
/// </para>
/// <para>
/// This is the minimum faithful replica of the DagEdit adapter seam, using only
/// test-assembly types (<see cref="LifecycleMutableItem"/>). No DagEdit repo reference.
/// </para>
/// </summary>
internal sealed class ViewerProjectionAdapter
{
    private readonly List<LifecycleMutableItem> _items = new();

    /// <summary>Fires after every mutation. Consumer rebuilds the snapshot on this event.</summary>
    public event EventHandler? ProjectionChanged;

    public IReadOnlyList<LifecycleMutableItem> Items => _items;

    /// <summary>
    /// Builds a new <see cref="SpatialIndex"/> from the current item list.
    /// Inserts the SAME <see cref="LifecycleMutableItem"/> references so that
    /// VCA's <c>_visualMap</c> (reference equality) can find them across rebuilds.
    /// </summary>
    public ISpatialIndex BuildSnapshot()
    {
        var snapshot = new SpatialIndex { Extent = new VCRect(-500, -500, 3000, 2000) };
        foreach (var item in _items)
            snapshot.Insert(item);
        return snapshot;
    }

    public void Add(LifecycleMutableItem item)    { _items.Add(item); Notify(); }
    public void Remove(string id)                 { if (_items.RemoveAll(x => x.Id == id) > 0) Notify(); }
    public void Move(string id, VCRect newBounds)
    {
        var item = _items.Find(x => x.Id == id);
        if (item != null) { item.Bounds = newBounds; Notify(); }
    }
    public void SetVisible(string id, bool visible)
    {
        var item = _items.Find(x => x.Id == id);
        if (item != null) { item.IsVisible = visible; Notify(); }
    }

    private void Notify() => ProjectionChanged?.Invoke(this, EventArgs.Empty);
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Phase F-1 integration test: verifies that the DagEdit-side
/// <c>adapter.ProjectionChanged += (_, _) =&gt; canvas.Items = adapter.BuildSnapshot()</c>
/// pattern works correctly with an actual <see cref="VCCanvas"/> instance.
/// <para>
/// Key questions answered by each test:
/// <list type="number">
///   <item><see cref="Integration_Add_ItemRealized"/> —
///         BuildSnapshot() + Items assignment causes new item to appear.</item>
///   <item><see cref="Integration_Remove_ItemVirtualized_OtherItemRetained"/> —
///         Removed item is virtualized; retained item keeps its Control.</item>
///   <item><see cref="Integration_Move_SameRefReuse_Confirmed"/> —
///         In-place Bounds mutation + BuildSnapshot() → same Control instance reused.</item>
///   <item><see cref="Integration_MultipleSnapshots_SameRefs_ControlsAlwaysReused"/> —
///         Repeated BuildSnapshot() calls never re-realize items that haven't changed.</item>
///   <item><see cref="Integration_NewObject_SameId_RefReuseBreaks"/> —
///         Replacing item with new object (same Id) breaks Control reuse — links E-3 evidence.</item>
/// </list>
/// </para>
/// </summary>
public class VirtualCanvasIntegrationTests
{
    // Flush all Input-priority dispatcher work before asserting.
    private static DispatcherOperation Flush()
        => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private static LifecycleMutableItem MakeItem(string id, string label, VCRect bounds)
        => new() { Id = id, Label = label, Bounds = bounds, ZIndex = 1 };

    /// <summary>
    /// Wires adapter → canvas in the canonical DagEdit integration pattern:
    /// one line, BuildSnapshot() called on every ProjectionChanged.
    /// </summary>
    private static void Wire(ViewerProjectionAdapter adapter, VCCanvas canvas)
        => adapter.ProjectionChanged += (_, _) => canvas.Items = adapter.BuildSnapshot();

    // ── 1. Add ───────────────────────────────────────────────────────────────

    /// <summary>
    /// adapter.Add() → ProjectionChanged → BuildSnapshot() → canvas.Items = snapshot
    /// must result in the new item being realized in the actual VirtualCanvas instance.
    /// </summary>
    [AvaloniaFact]
    public async Task Integration_Add_ItemRealized()
    {
        var adapter = new ViewerProjectionAdapter();
        var canvas  = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };
        Wire(adapter, canvas);

        var item = MakeItem("N1", "Source", new VCRect(0, 0, 200, 60));
        adapter.Add(item);   // → ProjectionChanged → BuildSnapshot() → canvas.Items
        await Flush();

        var realized = canvas.VisualFromItem(item);
        Assert.NotNull(realized);
        Assert.Equal("Source", (string?)realized.Tag);
    }

    // ── 2. Remove ────────────────────────────────────────────────────────────

    /// <summary>
    /// adapter.Remove() causes the removed item's Control to be virtualized;
    /// retained items must keep their Controls unchanged.
    /// </summary>
    [AvaloniaFact]
    public async Task Integration_Remove_ItemVirtualized_OtherItemRetained()
    {
        var adapter = new ViewerProjectionAdapter();
        var canvas  = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };
        Wire(adapter, canvas);

        var keep   = MakeItem("N1", "Keep",   new VCRect(  0, 0, 200, 60));
        var remove = MakeItem("N2", "Remove", new VCRect(260, 0, 200, 60));
        adapter.Add(keep);
        adapter.Add(remove);
        await Flush();

        var keepControl = canvas.VisualFromItem(keep);
        Assert.NotNull(keepControl);
        Assert.NotNull(canvas.VisualFromItem(remove));

        adapter.Remove("N2");   // → snapshot without remove
        await Flush();

        // Retained item: same Control instance.
        Assert.Same(keepControl, canvas.VisualFromItem(keep));

        // Removed item: no longer in _visualMap.
        Assert.Null(canvas.VisualFromItem(remove));
    }

    // ── 3. Move (same-ref reuse) ──────────────────────────────────────────────

    /// <summary>
    /// adapter.Move() mutates Bounds in-place on the SAME object reference.
    /// BuildSnapshot() inserts that same reference → VCA's _visualMap hits →
    /// the existing Control is reused (not re-created).
    /// <para>
    /// This is the critical same-ref reuse assertion for the DagEdit integration path:
    /// if this fails, every move would destroy animation/focus state on the node's Control.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Integration_Move_SameRefReuse_Confirmed()
    {
        var adapter = new ViewerProjectionAdapter();
        var canvas  = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };
        Wire(adapter, canvas);

        var item = MakeItem("N1", "Node", new VCRect(0, 0, 200, 60));
        adapter.Add(item);
        await Flush();

        var originalControl = canvas.VisualFromItem(item);
        Assert.NotNull(originalControl);

        // Act: move via adapter (in-place Bounds mutation, same object reference).
        adapter.Move("N1", new VCRect(400, 120, 200, 60));
        // → ProjectionChanged
        // → BuildSnapshot(): inserts same item reference with new Bounds
        // → canvas.Items = snapshot
        // → RealizeOverride: _visualMap.TryGetValue(item) hits → Control REUSED
        await Flush();

        var movedControl = canvas.VisualFromItem(item);
        Assert.NotNull(movedControl);

        // Same Control instance: _visualMap hit via reference equality.
        Assert.Same(originalControl, movedControl);
        Assert.Equal("Node", (string?)movedControl.Tag);
    }

    // ── 4. Multiple BuildSnapshot() calls — stable refs always hit ────────────

    /// <summary>
    /// Each mutation triggers a full BuildSnapshot() rebuild.
    /// As long as the same item references are used, Controls must always be reused:
    /// three consecutive moves must not re-create the Controls.
    /// <para>
    /// Verifies the invariant: N BuildSnapshot() calls with the same refs → 0 additional
    /// factory.Realize calls after the initial realization.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Integration_MultipleSnapshots_SameRefs_ControlsAlwaysReused()
    {
        var factory = new IntegrationCountingFactory();
        var adapter = new ViewerProjectionAdapter();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };
        Wire(adapter, canvas);

        var item = MakeItem("N1", "Node", new VCRect(0, 0, 200, 60));
        adapter.Add(item);
        await Flush();

        Assert.Equal(1, factory.RealizeCallCount);   // one realize on add

        var control = canvas.VisualFromItem(item);
        Assert.NotNull(control);

        // Three consecutive moves → three BuildSnapshot() rebuilds.
        adapter.Move("N1", new VCRect(100, 0, 200, 60));
        await Flush();
        adapter.Move("N1", new VCRect(200, 0, 200, 60));
        await Flush();
        adapter.Move("N1", new VCRect(300, 0, 200, 60));
        await Flush();

        // No re-realizes: _visualMap hit on every BuildSnapshot().
        Assert.Equal(1, factory.RealizeCallCount);

        // Same Control throughout.
        Assert.Same(control, canvas.VisualFromItem(item));
    }

    // ── 5. New object (same Id) breaks ref reuse — E-3 link ──────────────────

    /// <summary>
    /// If the adapter replaces an item with a NEW object instance (same logical Id,
    /// different reference), VCA's _visualMap misses → old Control virtualized,
    /// new Control created. This links Phase E-3 evidence to the actual integration path.
    /// <para>
    /// In practice, DagEdit must NOT replace stable item objects to avoid this penalty.
    /// The adapter must maintain stable references across BuildSnapshot() calls.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Integration_NewObject_SameId_RefReuseBreaks()
    {
        var factory = new IntegrationCountingFactory();
        var adapter = new ViewerProjectionAdapter();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };
        Wire(adapter, canvas);

        var item1 = MakeItem("N1", "Original", new VCRect(0, 0, 200, 60));
        adapter.Add(item1);
        await Flush();

        var control1 = canvas.VisualFromItem(item1);
        Assert.NotNull(control1);
        Assert.Equal(1, factory.RealizeCallCount);

        // Act: simulate an adapter that creates a NEW object for the same logical node.
        // (This is the anti-pattern DagEdit must avoid.)
        adapter.Remove("N1");
        var item1b = MakeItem("N1", "Original", new VCRect(400, 0, 200, 60));
        // Same Id, different reference — _visualMap will miss.
        Assert.Equal(item1.Id, item1b.Id);
        Assert.False(ReferenceEquals(item1, item1b));
        adapter.Add(item1b);
        await Flush();

        // Old Control is gone.
        Assert.Null(canvas.VisualFromItem(item1));

        // New Control was created for item1b.
        var control1b = canvas.VisualFromItem(item1b);
        Assert.NotNull(control1b);
        Assert.NotSame(control1, control1b);

        // Realize was called twice: once for item1, once for item1b.
        Assert.Equal(2, factory.RealizeCallCount);
    }
}
