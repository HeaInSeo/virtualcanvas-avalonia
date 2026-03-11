using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VirtualCanvas.Avalonia.Factories;
using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;
using Xunit;

using VCCanvas = VirtualCanvas.Avalonia.Controls.VirtualCanvas;

namespace VirtualCanvas.Avalonia.Tests;

// ── file-scoped: pinning test harness ────────────────────────────────────────

file sealed class PinTestFactory : IVisualFactory
{
    public int RealizeCallCount    { get; private set; }
    public int VirtualizeCallCount { get; private set; }

    public void BeginRealize() { }
    public void EndRealize()   { }

    public Control? Realize(ISpatialItem item, bool force)
    {
        RealizeCallCount++;
        return item is LifecycleMutableItem p ? new Border { Tag = p.Label } : null;
    }

    public bool Virtualize(Control visual) { VirtualizeCallCount++; return true; }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Phase I-2 pinning contract tests.
/// Verifies the minimal <see cref="VCCanvas.Pin"/>/<see cref="VCCanvas.Unpin"/> API
/// against all relevant lifecycle paths.
/// <para>
/// Pinning contract (summary):
/// <list type="number">
///   <item>A pinned item's Control is never removed by <c>ShouldVirtualize</c>.</item>
///   <item>Pinning survives snapshot rebuilds and viewbox changes.</item>
///   <item>Teardown (<c>Items==null</c>) and <c>IsVisible=false</c> override pinning.</item>
///   <item><c>ForceVirtualizeItem</c> bypasses pinning (caller's explicit intent).</item>
///   <item>Pin identity is reference-based; replacing the item object clears the pin.</item>
///   <item>Unpin restores normal virtualization on the next realization pass.</item>
/// </list>
/// </para>
/// </summary>
public class PinningContractTests
{
    private static DispatcherOperation Flush()
        => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private static LifecycleMutableItem MakeItem(string id, string label, VCRect bounds)
        => new() { Id = id, Label = label, Bounds = bounds, ZIndex = 1 };

    private static SpatialIndex MakeIndex(params LifecycleMutableItem[] items)
    {
        var idx = new SpatialIndex { Extent = new VCRect(-500, -500, 3000, 2000) };
        foreach (var n in items) idx.Insert(n);
        return idx;
    }

    private static SpatialIndex EmptyIndex()
        => new SpatialIndex { Extent = new VCRect(-500, -500, 3000, 2000) };

    // ── 1. Pin prevents normal virtualization ─────────────────────────────────

    /// <summary>
    /// A pinned item's Control survives when the item leaves a non-empty snapshot.
    /// Other (unpinned) items in the same pass are virtualized normally.
    /// </summary>
    [AvaloniaFact]
    public async Task Pin_PreventsVirtualization_WhenItemLeavesSnapshot()
    {
        var factory = new PinTestFactory();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };

        var pinned   = MakeItem("N1", "Pinned",   new VCRect(  0, 0, 200, 60));
        var unpinned = MakeItem("N2", "Unpinned", new VCRect(300, 0, 200, 60));

        canvas.Items = MakeIndex(pinned, unpinned);
        await Flush();

        var pinnedControl   = canvas.VisualFromItem(pinned);
        var unpinnedControl = canvas.VisualFromItem(unpinned);
        Assert.NotNull(pinnedControl);
        Assert.NotNull(unpinnedControl);

        // Pin before removing.
        canvas.Pin(pinned);
        Assert.True(canvas.IsPinned(pinned));
        Assert.False(canvas.IsPinned(unpinned));

        // Remove both from snapshot (new snapshot has only a third item).
        var other = MakeItem("N3", "Other", new VCRect(600, 0, 200, 60));
        canvas.Items = MakeIndex(other);
        await Flush();

        // Pinned item: Control STAYS (pin protected it).
        Assert.Same(pinnedControl, canvas.VisualFromItem(pinned));

        // Unpinned item: Control is gone.
        Assert.Null(canvas.VisualFromItem(unpinned));

        // Other item: realized normally.
        Assert.NotNull(canvas.VisualFromItem(other));
    }

    // ── 2. Pin prevents virtualization on empty-snapshot fast path ────────────

    /// <summary>
    /// When <c>canvas.Items = emptySnapshot</c>, the synchronous fast path in
    /// <c>Items_Changed</c> skips pinned items.
    /// <para>
    /// Unpinned items are removed (and <c>factory.Virtualize</c> is called for them).
    /// Pinned items remain in <c>_visualMap</c> and the visual tree.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Pin_PreventsVirtualization_OnEmptySnapshot()
    {
        var factory = new PinTestFactory();
        var canvas  = new VCCanvas { VisualFactory = factory, IsVirtualizing = false };

        var pinned   = MakeItem("N1", "Pinned",   new VCRect(  0, 0, 200, 60));
        var unpinned = MakeItem("N2", "Unpinned", new VCRect(300, 0, 200, 60));

        canvas.Items = MakeIndex(pinned, unpinned);
        await Flush();

        var pinnedControl = canvas.VisualFromItem(pinned);
        Assert.Equal(2, factory.RealizeCallCount);
        Assert.Equal(0, factory.VirtualizeCallCount);

        canvas.Pin(pinned);

        // Empty snapshot → fast path.
        canvas.Items = EmptyIndex();
        await Flush();

        // Pinned item: still in visual tree (fast path skipped it).
        Assert.Same(pinnedControl, canvas.VisualFromItem(pinned));

        // Unpinned item: removed (factory.Virtualize called once via fast path).
        Assert.Null(canvas.VisualFromItem(unpinned));
        Assert.Equal(1, factory.VirtualizeCallCount);  // only unpinned item triggered cleanup
    }

    // ── 3. Unpin restores normal virtualization ───────────────────────────────

    /// <summary>
    /// After <see cref="VCCanvas.Unpin"/>, the item is subject to normal virtualization
    /// on the next realization pass. If the item is absent from the snapshot at that
    /// point, it will be virtualized.
    /// </summary>
    [AvaloniaFact]
    public async Task Unpin_RestoresVirtualizationBehavior()
    {
        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };

        var item = MakeItem("N1", "Target", new VCRect(0, 0, 200, 60));
        var keep = MakeItem("N2", "Keep",   new VCRect(300, 0, 200, 60));

        canvas.Items = MakeIndex(item, keep);
        await Flush();

        canvas.Pin(item);

        // Remove item: stays due to pin.
        canvas.Items = MakeIndex(keep);
        await Flush();
        Assert.NotNull(canvas.VisualFromItem(item));

        // Unpin: item is now a candidate for virtualization.
        canvas.Unpin(item);
        Assert.False(canvas.IsPinned(item));

        // Trigger a new realization pass (InvalidateReality).
        // Item is still absent from snapshot → will be virtualized.
        canvas.InvalidateReality();
        await Flush();

        Assert.Null(canvas.VisualFromItem(item));   // virtualized after unpin
        Assert.NotNull(canvas.VisualFromItem(keep)); // keep unaffected
    }

    // ── 4. Pin is ref-based: new object instance is not pinned ───────────────

    /// <summary>
    /// Pin identity is reference-based (same as <c>_visualMap</c>).
    /// Pinning item1 and then replacing it with item1b (same Id, different reference)
    /// does NOT transfer the pin to item1b.
    /// <para>
    /// The consumer must pin the new object explicitly after a re-add.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Pin_IsRefBased_NewObjectSameId_NotPinned()
    {
        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };

        var item1 = MakeItem("N1", "Original", new VCRect(0, 0, 200, 60));
        canvas.Items = MakeIndex(item1);
        await Flush();

        // Pin item1 by reference.
        canvas.Pin(item1);
        Assert.True(canvas.IsPinned(item1));

        // Replace with new object (same Id, different reference).
        var item1b = MakeItem("N1", "Original", new VCRect(0, 50, 200, 60));
        Assert.False(ReferenceEquals(item1, item1b));

        canvas.Items = MakeIndex(item1b);
        await Flush();

        // item1b is NOT pinned (pin was on item1's reference, not on item1b's).
        Assert.False(canvas.IsPinned(item1b));

        // item1 is STILL pinned (pin set holds item1's reference).
        Assert.True(canvas.IsPinned(item1));

        // item1 stays in _visualMap because the pin protects it even though it left the snapshot.
        // Consumer must call Unpin(item1) explicitly if the old control should be released.
        Assert.NotNull(canvas.VisualFromItem(item1));

        // item1b was realized normally (no pin transfer).
        Assert.NotNull(canvas.VisualFromItem(item1b));
    }

    // ── 5. ForceVirtualizeItem bypasses pinning AND clears stale pin ─────────

    /// <summary>
    /// <see cref="VCCanvas.ForceVirtualizeItem(ISpatialItem)"/> bypasses pinning
    /// and also clears the pin entry (stale pin cleanup, I-3).
    /// <para>
    /// <b>I-3 contract:</b> force removal = caller override. Overriding pin implicitly
    /// means the consumer no longer needs the protection, so the pin is cleaned up
    /// automatically. No stale entry remains in <c>_pinnedItems</c>.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task ForceVirtualize_BypassesPinning_AndClearsStalePinEntry()
    {
        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };

        var item = MakeItem("N1", "Pinned", new VCRect(0, 0, 200, 60));
        canvas.Items = MakeIndex(item);
        await Flush();

        canvas.Pin(item);
        Assert.True(canvas.IsPinned(item));
        Assert.NotNull(canvas.VisualFromItem(item));

        // ForceVirtualizeItem: removes visual AND clears pin entry (I-3).
        canvas.ForceVirtualizeItem(item);

        Assert.Null(canvas.VisualFromItem(item));   // removed despite pin
        Assert.False(canvas.IsPinned(item));         // pin set auto-cleared (no stale entry)
    }

    // ── 6. IsVisible=false overrides pin ─────────────────────────────────────

    /// <summary>
    /// <c>item.IsVisible = false</c> overrides pinning: <c>ShouldVirtualize</c>
    /// returns <c>true</c> before the pin check is reached.
    /// <para>
    /// Explicit hide is a stronger semantic than pin. A hidden item that is pinned
    /// will still be virtualized on the next realization pass.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Pin_Overridden_ByIsVisibleFalse()
    {
        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };

        var item = MakeItem("N1", "Toggle", new VCRect(0, 0, 200, 60));
        canvas.Items = MakeIndex(item);
        await Flush();

        canvas.Pin(item);
        Assert.NotNull(canvas.VisualFromItem(item));

        // Hide: IsVisible = false overrides pin.
        item.IsVisible = false;
        canvas.InvalidateReality();
        await Flush();

        // Item was virtualized because IsVisible=false wins over pin.
        Assert.Null(canvas.VisualFromItem(item));
    }
}
