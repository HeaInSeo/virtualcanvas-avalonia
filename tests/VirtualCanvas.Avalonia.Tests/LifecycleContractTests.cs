using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VirtualCanvas.Avalonia.Factories;
using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;
using Xunit;

using VCCanvas = VirtualCanvas.Avalonia.Controls.VirtualCanvas;

namespace VirtualCanvas.Avalonia.Tests;

// ── file-scoped: lifecycle observation harness ────────────────────────────────

/// <summary>
/// Tracks factory calls and distinguishes between the two remove paths:
/// <list type="bullet">
///   <item>Normal path (RealizeOverride): <see cref="Virtualize"/> IS called.</item>
///   <item>Force path (empty-snapshot fast path): <see cref="Virtualize"/> is NOT called.</item>
/// </list>
/// <para>
/// VirtualizeCallCount counts calls to <see cref="Virtualize"/>.
/// Since ShouldVirtualize is invoked twice per item per cycle (collection phase +
/// execution phase), each normally-virtualized item contributes 2 to the count.
/// A ForceVirtualize call contributes 0.
/// </para>
/// </summary>
file sealed class LifecycleObservingFactory : IVisualFactory
{
    public int RealizeCallCount   { get; private set; }
    public int VirtualizeCallCount { get; private set; }

    public void BeginRealize() { }
    public void EndRealize()   { }

    public Control? Realize(ISpatialItem item, bool force)
    {
        RealizeCallCount++;
        return item is LifecycleMutableItem p ? new Border { Tag = p.Label } : null;
    }

    public bool Virtualize(Control visual)
    {
        VirtualizeCallCount++;
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Phase I-0: VCA visual lifecycle contract observation.
/// <para>
/// Documents what VCA currently guarantees (and does not guarantee) about the
/// realize / virtualize / force-remove lifecycle. This is the contract-gap analysis
/// needed before a DagEdit Hybrid (Phase 2) integration.
/// </para>
/// <para>
/// Key findings:
/// <list type="number">
///   <item>Normal remove → <c>factory.Virtualize</c> called (via <c>ShouldVirtualize</c>).</item>
///   <item>Empty-snapshot fast path → <c>factory.Virtualize</c> NOT called (pool cleanup gap).</item>
///   <item><c>SelectedItem</c> has NO implicit virtualization protection (unlike keyboard focus).</item>
///   <item><c>IsVirtualizing=false</c> → all items in <c>Items</c> are always realized.</item>
///   <item>Remove + re-add with new object → two Controls (old gone, new created).</item>
///   <item>Visual tree membership is synchronous; factory notification is async.</item>
/// </list>
/// </para>
/// </summary>
public class LifecycleContractTests
{
    private static DispatcherOperation Flush()
        => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private static LifecycleMutableItem MakeItem(string id, string label, VCRect bounds)
        => new() { Id = id, Label = label, Bounds = bounds, ZIndex = 1 };

    // Helper: SpatialIndex with a finite extent (PriorityQuadTree rejects ±Inf).
    private static SpatialIndex MakeIndex(params LifecycleMutableItem[] items)
    {
        var idx = new SpatialIndex { Extent = new VCRect(-500, -500, 3000, 2000) };
        foreach (var n in items) idx.Insert(n);
        return idx;
    }

    private static SpatialIndex EmptyIndex()
        => new SpatialIndex { Extent = new VCRect(-500, -500, 3000, 2000) };

    // ── 1. Add: realized Control appears in visual tree ───────────────────────

    /// <summary>
    /// After Add and flush, the Control returned by factory.Realize must be present
    /// in the canvas's visual children (<see cref="VCCanvas.GetVisualChildren"/>).
    /// <para>
    /// Guarantees: visual tree membership is established before the async pass completes.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Lifecycle_Add_ControlPresentInVisualTree()
    {
        var factory = new LifecycleObservingFactory();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };

        var item = MakeItem("N1", "Source", new VCRect(0, 0, 200, 60));
        canvas.Items = MakeIndex(item);
        await Flush();

        var realized = canvas.VisualFromItem(item);
        Assert.NotNull(realized);

        // Visual tree membership.
        var visuals = canvas.GetVisualChildren().ToList();
        Assert.Contains(realized, visuals);
        Assert.Equal(1, factory.RealizeCallCount);
    }

    // ── 2. Normal remove: factory.Virtualize called ───────────────────────────

    /// <summary>
    /// When an item leaves the snapshot (non-empty new snapshot), the normal
    /// <c>RealizeOverride</c> path runs: the item is not in <c>realizedItems</c>,
    /// so <c>ShouldVirtualize</c> is called → <c>factory.Virtualize</c> fires.
    /// <para>
    /// <b>Observation:</b> <c>ShouldVirtualize</c> is invoked twice per virtualized item
    /// (collection phase + execution phase), so <c>VirtualizeCallCount</c> = 2 per item.
    /// </para>
    /// <para>
    /// This guarantees the factory/pool cleanup callback is invoked on the normal path.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Lifecycle_NormalRemove_FactoryVirtualizeCalled_Twice()
    {
        var factory = new LifecycleObservingFactory();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };

        var item = MakeItem("N1", "A", new VCRect(0, 0, 200, 60));
        var keep = MakeItem("N2", "B", new VCRect(300, 0, 200, 60));
        canvas.Items = MakeIndex(item, keep);
        await Flush();

        Assert.Equal(2, factory.RealizeCallCount);
        Assert.Equal(0, factory.VirtualizeCallCount);

        // Act: remove item from snapshot (keep is still present → non-empty snapshot).
        canvas.Items = MakeIndex(keep);
        await Flush();

        // Normal path: ShouldVirtualize called twice per virtualized item.
        // factory.Virtualize returns true both times → VirtualizeCallCount = 2.
        Assert.Equal(2, factory.VirtualizeCallCount);

        // Item is gone from visual tree.
        Assert.Null(canvas.VisualFromItem(item));
        Assert.NotNull(canvas.VisualFromItem(keep));
    }

    // ── 3. Empty-snapshot fast path: factory.Virtualize NOW called ──────────────

    /// <summary>
    /// When <c>canvas.Items = emptySnapshot</c>, <c>Items_Changed</c> detects
    /// <c>!Items.Any()</c> and immediately removes all realized visuals.
    /// <para>
    /// <b>Fixed (I-1):</b> <c>factory.Virtualize</c> is now called once per item
    /// before <c>ForceVirtualizeItem</c>, ensuring pool cleanup runs on this path too.
    /// </para>
    /// <para>
    /// Call count difference vs normal remove:
    /// <list type="bullet">
    ///   <item>Normal remove: <c>factory.Virtualize</c> called <b>2×</b> per item
    ///         (collection phase + execution phase via <c>ShouldVirtualize</c>).</item>
    ///   <item>Empty-snapshot fast path: <c>factory.Virtualize</c> called <b>1×</b>
    ///         per item (no collection phase — direct loop in <c>Items_Changed</c>).</item>
    /// </list>
    /// The semantic guarantee (cleanup callback fires) is now consistent across both paths.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Lifecycle_EmptySnapshot_ForceVirtualize_FactoryVirtualizeCalledOnce()
    {
        var factory = new LifecycleObservingFactory();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };

        var item = MakeItem("N1", "A", new VCRect(0, 0, 200, 60));
        canvas.Items = MakeIndex(item);
        await Flush();

        Assert.Equal(1, factory.RealizeCallCount);
        Assert.Equal(0, factory.VirtualizeCallCount);

        // Act: replace with EMPTY snapshot → Items_Changed fast path.
        canvas.Items = EmptyIndex();
        await Flush();

        // Item is gone from _visualMap and visual tree.
        Assert.Null(canvas.VisualFromItem(item));

        // factory.Virtualize called once per item on the fast path (no double-call).
        Assert.Equal(1, factory.VirtualizeCallCount);
    }

    // ── 4. Remove + re-add with new object ───────────────────────────────────

    /// <summary>
    /// Remove item, then re-add with a NEW object (same Id, different reference).
    /// Simulates a projection flush that recreates item objects.
    /// <para>
    /// Expected: two Realize calls, one Virtualize cycle. The old Control is gone;
    /// the new item gets a fresh Control. The old object reference is evicted from
    /// <c>_visualMap</c>; the new object is inserted.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Lifecycle_RemoveReAdd_NewObject_TwoRealizations_OldControlGone()
    {
        var factory = new LifecycleObservingFactory();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };

        var item1 = MakeItem("N1", "Original", new VCRect(0, 0, 200, 60));
        var keep  = MakeItem("N2", "Keep",     new VCRect(300, 0, 200, 60));
        canvas.Items = MakeIndex(item1, keep);
        await Flush();

        var control1 = canvas.VisualFromItem(item1);
        Assert.NotNull(control1);
        Assert.Equal(2, factory.RealizeCallCount);

        // Act: remove item1, add item1b (same Id, new object) in one snapshot swap.
        var item1b = MakeItem("N1", "Original", new VCRect(0, 50, 200, 60));
        Assert.Equal(item1.Id, item1b.Id);
        Assert.False(ReferenceEquals(item1, item1b));

        canvas.Items = MakeIndex(item1b, keep);
        await Flush();

        // Old item: evicted from _visualMap and visual tree.
        Assert.Null(canvas.VisualFromItem(item1));

        // New item: a fresh Control was created.
        var control1b = canvas.VisualFromItem(item1b);
        Assert.NotNull(control1b);
        Assert.NotSame(control1, control1b);

        // Two realizes for item1 + item1b; keep was reused (no extra realize).
        Assert.Equal(3, factory.RealizeCallCount);  // item1 + item1b + keep(initial)

        // keep's Control was reused (same reference).
        Assert.NotNull(canvas.VisualFromItem(keep));
    }

    // ── 5. SelectedItem has NO virtualization protection ─────────────────────

    /// <summary>
    /// <b>Hybrid risk:</b>
    /// VCA's <c>SelectedItem</c> property does NOT protect an item from being virtualized.
    /// <c>ShouldVirtualize</c> only checks <c>visual.IsFocused</c> (keyboard focus),
    /// not whether the item is the current <c>SelectedItem</c>.
    /// <para>
    /// If DagEdit sets <c>canvas.SelectedItem</c> for a selected node and that node's
    /// item reference leaves the snapshot, VCA will virtualize its Control on the next pass.
    /// This means DagEdit must either:
    /// <list type="bullet">
    ///   <item>Keep selected items in the snapshot at all times, OR</item>
    ///   <item>Use keyboard focus (e.g., <c>visual.Focus()</c>) to prevent virtualization, OR</item>
    ///   <item>Wait for a future VCA pinning API.</item>
    /// </list>
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Lifecycle_SelectedItem_NotProtectedFromVirtualization()
    {
        var factory = new LifecycleObservingFactory();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };

        var item = MakeItem("N1", "Selected", new VCRect(0, 0, 200, 60));
        var keep = MakeItem("N2", "Keep",     new VCRect(300, 0, 200, 60));
        canvas.Items = MakeIndex(item, keep);
        await Flush();

        // Simulate: user selected item.
        canvas.SelectedItem = item;
        Assert.Same(item, canvas.SelectedItem);

        var selectedControl = canvas.VisualFromItem(item);
        Assert.NotNull(selectedControl);

        // Act: remove item from snapshot (keep remains → non-empty path).
        canvas.Items = MakeIndex(keep);
        await Flush();

        // !! SelectedItem was virtualized despite being "selected".
        // SelectedItem property on canvas is not automatically cleared by VCA.
        Assert.Null(canvas.VisualFromItem(item));  // Control is gone from visual tree
        // Note: canvas.SelectedItem may still reference item even after virtualization.
        // VCA does not auto-clear SelectedItem when the item's Control is removed.
        Assert.Same(item, canvas.SelectedItem);    // stale reference — canvas did not clear it
    }

    // ── 6. IsVirtualizing=false guarantees all Items always realized ──────────

    /// <summary>
    /// With <c>IsVirtualizing=false</c>, <c>RealizeOverride</c> enumerates all items
    /// in <c>Items</c> (not just viewbox-intersecting ones). Every item in the snapshot
    /// is in <c>realizedItems</c>, so none are candidates for virtualization.
    /// <para>
    /// This is the safe baseline for the DagEdit integration: with virtualization off,
    /// Offset/Scale changes never cause Controls to be virtualized. Items stay realized
    /// until explicitly removed from the snapshot.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Lifecycle_IsVirtualizingFalse_AllItems_AlwaysRealized()
    {
        var factory = new LifecycleObservingFactory();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };

        var items = new[]
        {
            MakeItem("N1", "A", new VCRect(   0,    0, 200, 60)),
            MakeItem("N2", "B", new VCRect( 300,    0, 200, 60)),
            MakeItem("N3", "C", new VCRect(   0, 200, 200, 60)),
            MakeItem("N4", "D", new VCRect( 300, 200, 200, 60)),
        };

        canvas.Items = MakeIndex(items);
        await Flush();

        Assert.Equal(4, factory.RealizeCallCount);
        Assert.Equal(0, factory.VirtualizeCallCount);

        // Simulate a large pan (offset change) that would normally push all items
        // out of the viewbox. With IsVirtualizing=false, nothing is virtualized.
        canvas.Offset = new Point(5000, 5000);
        await Flush();

        // All items remain realized.
        foreach (var item in items)
            Assert.NotNull(canvas.VisualFromItem(item));

        // No additional realize calls (Controls reused).
        Assert.Equal(4, factory.RealizeCallCount);
        Assert.Equal(0, factory.VirtualizeCallCount);
    }
}
