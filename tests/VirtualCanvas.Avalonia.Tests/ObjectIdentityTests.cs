using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VirtualCanvas.Avalonia.Factories;
using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;
using Xunit;

using VCCanvas = VirtualCanvas.Avalonia.Controls.VirtualCanvas;

namespace VirtualCanvas.Avalonia.Tests;

// ── file-scoped: test-internal only ──────────────────────────────────────────

/// <summary>
/// Counts factory calls so tests can verify whether VCA called Realize again
/// (new Control created) or skipped it (Control reused from _visualMap).
/// <para>
/// <c>Virtualize</c> is also counted: it is called from <c>ShouldVirtualize</c>
/// only when an item exits the realized set but is still visible and not focused.
/// </para>
/// </summary>
file sealed class CountingViewerFactory : IVisualFactory
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
/// Phase E-3 spike: object identity vs value identity.
/// <para>
/// VCA tracks items in <c>Dictionary&lt;ISpatialItem, Control&gt;</c>
/// (field <c>_visualMap</c>) using <em>reference equality</em> — the default
/// for reference types that do not override <c>Equals</c>/<c>GetHashCode</c>.
/// </para>
/// <para>
/// Consequence:
/// <list type="bullet">
///   <item>Same object reference → <c>TryGetValue</c> hits → Control REUSED.</item>
///   <item>New object instance (same logical Id) → <c>TryGetValue</c> misses →
///         new Control created; old item's Control is virtualized.</item>
/// </list>
/// </para>
/// These tests produce the test-result evidence for the design decision:
/// "Does the DagEdit projection seam need to maintain stable object references,
/// or must VCA gain an ID-based lookup path?"
/// </summary>
public class ObjectIdentityTests
{
    private static DispatcherOperation Flush()
        => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private static LifecycleMutableItem MakeItem(string id, string label, VCRect bounds)
        => new() { Id = id, Label = label, Bounds = bounds, ZIndex = 1 };

    // Helper: create a snapshot index from a set of items (finite extent required).
    private static LifecycleDynamicIndex Snapshot(params LifecycleMutableItem[] items)
    {
        var idx = new LifecycleDynamicIndex();
        foreach (var n in items) idx.Add(n);
        return idx;
    }

    // ── Case A: Same object reference ─────────────────────────────────────────

    /// <summary>
    /// When the projection source mutates <see cref="LifecycleMutableItem.Bounds"/>
    /// in-place (same object reference) and rebuilds the snapshot, VCA finds the item
    /// in <c>_visualMap</c> via reference equality → Control is REUSED, Realize not called.
    /// </summary>
    [AvaloniaFact]
    public async Task CaseA_SameObjectReference_ControlReused_NoReRealize()
    {
        var factory = new CountingViewerFactory();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };

        var item1 = MakeItem("N1", "Node", new VCRect(0, 0, 200, 60));
        canvas.Items = Snapshot(item1);
        await Flush();

        var visual1 = canvas.VisualFromItem(item1);
        Assert.NotNull(visual1);
        Assert.Equal(1, factory.RealizeCallCount);       // one realize so far
        Assert.Equal(0, factory.VirtualizeCallCount);    // nothing virtualized

        // Act: mutate Bounds in-place (same object) and rebuild snapshot.
        item1.Bounds = new VCRect(400, 120, 200, 60);
        canvas.Items = Snapshot(item1);
        await Flush();

        // ── Assertions ──────────────────────────────────────────────────────────
        var visual2 = canvas.VisualFromItem(item1);
        Assert.NotNull(visual2);

        // Control reused: Dictionary<ISpatialItem,Control>.TryGetValue found item1
        // → existing Control returned, factory.Realize NOT called again.
        Assert.Same(visual1, visual2);
        Assert.Equal(1, factory.RealizeCallCount);       // unchanged: no re-realize
        Assert.Equal(0, factory.VirtualizeCallCount);    // item1 stays in realizedItems
    }

    // ── Case B: New object instance, same logical Id ───────────────────────────

    /// <summary>
    /// When the projection source creates a NEW <see cref="LifecycleMutableItem"/>
    /// object for the same logical node (same <c>Id</c>, different reference),
    /// VCA's reference-based lookup misses → old Control virtualized, new Control created.
    /// <para>
    /// This is the "object identity problem": even though the logical node is the same,
    /// VCA cannot detect the identity without a stable object reference.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task CaseB_NewObjectInstance_SameId_ControlNotReused_OldVirtualized()
    {
        var factory = new CountingViewerFactory();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };

        var item1 = MakeItem("N1", "Node", new VCRect(0, 0, 200, 60));
        canvas.Items = Snapshot(item1);
        await Flush();

        var visual1 = canvas.VisualFromItem(item1);
        Assert.NotNull(visual1);
        Assert.Equal(1, factory.RealizeCallCount);

        // Act: create a NEW object for the same logical node (same Id, different reference).
        // This simulates a DagEdit projection that recreates item objects on each flush.
        var item1b = MakeItem("N1", "Node", new VCRect(400, 120, 200, 60));
        // item1b.Id == item1.Id  but  !ReferenceEquals(item1, item1b)
        Assert.Equal(item1.Id, item1b.Id);
        Assert.False(ReferenceEquals(item1, item1b));

        canvas.Items = Snapshot(item1b);
        await Flush();

        // ── Assertions ──────────────────────────────────────────────────────────

        // Old item: its Control is now GONE (virtualized, not in new snapshot).
        Assert.Null(canvas.VisualFromItem(item1));

        // New item: a brand-new Control was created.
        var visual2 = canvas.VisualFromItem(item1b);
        Assert.NotNull(visual2);

        // Controls are DIFFERENT instances: _visualMap missed item1b.
        Assert.NotSame(visual1, visual2);

        // Realize was called twice: once for item1, once for item1b.
        Assert.Equal(2, factory.RealizeCallCount);

        // Virtualize was called TWICE for item1: ShouldVirtualize is invoked once during
        // the collection phase (RealizeOverride line ~500) and once inside VirtualizeItemsBatch
        // → VirtualizeItem → ShouldVirtualize. Both calls count against VirtualizeCallCount.
        Assert.Equal(2, factory.VirtualizeCallCount);
    }

    // ── Case C: Mixed — only some items replaced with new instances ────────────

    /// <summary>
    /// When a projection flush replaces only ONE item with a new object and keeps
    /// other items as the same references, VCA behaves correctly:
    /// <list type="bullet">
    ///   <item>Replaced item → old Control virtualized, new Control created.</item>
    ///   <item>Unchanged items (same references) → Controls reused, no re-realize.</item>
    /// </list>
    /// This shows the scope of the identity problem is proportional to how many
    /// new objects the DagEdit projection creates per flush.
    /// </summary>
    [AvaloniaFact]
    public async Task CaseC_MixedSnapshot_OnlyReplacedItemReRealized()
    {
        var factory = new CountingViewerFactory();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };

        var item1 = MakeItem("N1", "Source",    new VCRect(  0, 0, 200, 60));
        var item2 = MakeItem("N2", "Filter",    new VCRect(260, 0, 200, 60));
        var item3 = MakeItem("N3", "Sink",      new VCRect(520, 0, 200, 60));

        canvas.Items = Snapshot(item1, item2, item3);
        await Flush();

        var visual1 = canvas.VisualFromItem(item1);
        var visual2 = canvas.VisualFromItem(item2);
        var visual3 = canvas.VisualFromItem(item3);
        Assert.NotNull(visual1);
        Assert.NotNull(visual2);
        Assert.NotNull(visual3);
        Assert.Equal(3, factory.RealizeCallCount);   // one per item, initial pass

        // Act: replace item2 with a NEW object; keep item1 and item3 as-is.
        var item2b = MakeItem("N2", "Filter", new VCRect(260, 50, 200, 60));

        canvas.Items = Snapshot(item1, item2b, item3);  // item2 absent, item2b present
        await Flush();

        // item2: old Control gone; new Control created for item2b.
        Assert.Null(canvas.VisualFromItem(item2));
        var visual2b = canvas.VisualFromItem(item2b);
        Assert.NotNull(visual2b);
        Assert.NotSame(visual2, visual2b);

        // item1, item3: Controls REUSED (same references → _visualMap hit).
        Assert.Same(visual1, canvas.VisualFromItem(item1));
        Assert.Same(visual3, canvas.VisualFromItem(item3));

        // Realize: 3 initial + 1 for item2b = 4.
        Assert.Equal(4, factory.RealizeCallCount);

        // Virtualize: only item2 was dropped from the snapshot.
        // ShouldVirtualize is called twice per virtualized item (collection phase + execution phase).
        Assert.Equal(2, factory.VirtualizeCallCount);
    }
}
