using System.Collections;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VirtualCanvas.Avalonia.Factories;
using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;
using Xunit;

using VCCanvas = VirtualCanvas.Avalonia.Controls.VirtualCanvas;

namespace VirtualCanvas.Avalonia.Tests;

// ── test-internal types (internal: file-local not allowed in non-file member sigs) ──

/// <summary>
/// Mutable ISpatialItem for lifecycle tests.
/// Bounds and IsVisible are settable to simulate in-place updates from a projection source.
/// </summary>
internal sealed class LifecycleMutableItem : ISpatialItem
{
    public required string Id    { get; init; }
    public required string Label { get; init; }
    public VCRect  Bounds   { get; set; }   // mutable: remove→update→reinsert for moves
    public double  Priority { get; init; }
    public int     ZIndex   { get; init; }
    public bool    IsVisible { get; set; } = true;  // mutable: in-place hide/show
}

/// <summary>
/// List-backed ISpatialIndex that separates data mutations (Add/Remove) from
/// change notifications (RaiseChanged).
/// <para>
/// This models the DagEdit projection seam: the source can batch multiple mutations
/// and emit a single change notification. VCA receives nothing until RaiseChanged fires.
/// </para>
/// </summary>
internal sealed class LifecycleDynamicIndex : ISpatialIndex
{
    private readonly List<ISpatialItem> _items = new();

    public event EventHandler? Changed;
    public VCRect Extent => VCRect.Infinite;

    public void Add(ISpatialItem item)    => _items.Add(item);
    public void Remove(ISpatialItem item) => _items.Remove(item);
    public void RaiseChanged()            => Changed?.Invoke(this, EventArgs.Empty);

    public bool HasItemsIntersecting(VCRect b) => _items.Count > 0;
    public IEnumerable<ISpatialItem> GetItemsIntersecting(VCRect b) => _items;
    public bool HasItemsInside(VCRect b) => _items.Count > 0;
    public IEnumerable<ISpatialItem> GetItemsInside(VCRect b) => _items;

    public IEnumerator<ISpatialItem> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()          => GetEnumerator();
}

/// <summary>Viewer-only factory: realizes only LifecycleMutableItem.</summary>
internal sealed class LifecycleViewerFactory : IVisualFactory
{
    public void BeginRealize() { }
    public void EndRealize()   { }
    public bool Virtualize(Control visual) => true;

    public Control? Realize(ISpatialItem item, bool force)
        => item is LifecycleMutableItem p ? new Border { Tag = p.Label } : null;
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Phase E-1 lifecycle spike: verifies that VCA's viewer realization path
/// responds correctly to dynamic add / remove / visibility-toggle / move.
/// <para>
/// These tests answer the three core lifecycle questions:
/// <list type="number">
///   <item>Does ISpatialIndex.Changed → RaiseChanged() suffice as a trigger? (all tests)</item>
///   <item>Can add/remove/hide/move each be driven without editor semantics? (each test)</item>
///   <item>Does the move path preserve the existing Control instance? (test 4)</item>
/// </list>
/// </para>
/// </summary>
public class ProjectedLifecycleTests
{
    // Flush all pending Input-priority dispatcher work and let layout settle.
    // Input(5) > Background(4): realization batches complete before this returns.
    private static DispatcherOperation Flush()
        => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private static LifecycleMutableItem MakeItem(string id, string label, VCRect bounds)
        => new() { Id = id, Label = label, Bounds = bounds, ZIndex = 1 };

    // ── 1. Dynamic add ────────────────────────────────────────────────────────

    /// <summary>
    /// Adding a new projected item to the index and calling RaiseChanged() must
    /// result in VCA realizing the new item on the next pass.
    /// </summary>
    [AvaloniaFact]
    public async Task DynamicAdd_IncreasesRealizedCount()
    {
        var item1 = MakeItem("N1", "Source", new VCRect(  0, 0, 200, 60));
        var item2 = MakeItem("N2", "Added",  new VCRect(260, 0, 200, 60));

        var index  = new LifecycleDynamicIndex();
        index.Add(item1);

        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };
        canvas.Items = index;          // → Items_Changed → InvalidateReality (item1 only)
        await Flush();

        // Baseline.
        Assert.NotNull(canvas.VisualFromItem(item1));
        Assert.Null(canvas.VisualFromItem(item2));

        // Act: external source adds item2 and fires the change notification.
        index.Add(item2);
        index.RaiseChanged();
        await Flush();

        // Both items must now be realized.
        Assert.NotNull(canvas.VisualFromItem(item1));
        Assert.NotNull(canvas.VisualFromItem(item2));
        Assert.Equal("Added", (string?)canvas.VisualFromItem(item2)!.Tag);
    }

    // ── 2. Dynamic remove ─────────────────────────────────────────────────────

    /// <summary>
    /// Removing a projected item from the index and calling RaiseChanged() must
    /// virtualize only that item; other realized items must be unaffected.
    /// </summary>
    [AvaloniaFact]
    public async Task DynamicRemove_VirtualizesRemovedItem_KeepsOthers()
    {
        var item1 = MakeItem("N1", "Keep",   new VCRect(  0, 0, 200, 60));
        var item2 = MakeItem("N2", "Remove", new VCRect(260, 0, 200, 60));

        var index = new LifecycleDynamicIndex();
        index.Add(item1);
        index.Add(item2);

        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };
        canvas.Items = index;
        await Flush();

        Assert.NotNull(canvas.VisualFromItem(item1));
        Assert.NotNull(canvas.VisualFromItem(item2));

        // Act: remove item2 from the projection source.
        index.Remove(item2);
        index.RaiseChanged();
        await Flush();

        // item2 must be virtualized; item1 must remain realized.
        Assert.NotNull(canvas.VisualFromItem(item1));
        Assert.Null(canvas.VisualFromItem(item2));
    }

    // ── 3. Toggle visibility (hide → show) ────────────────────────────────────

    /// <summary>
    /// Toggling IsVisible=false then true — without remove/reinsert — must
    /// virtualize then re-realize the item.
    /// <para>
    /// This is the cheapest lifecycle op: the item stays in the index;
    /// VCA's pre-filter (line 392: <c>if (!item.IsVisible) return null</c>) and
    /// ShouldVirtualize (line 453: <c>!item.IsVisible → true</c>) handle the rest.
    /// A plain RaiseChanged() is the only signal needed.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task ToggleVisibility_HidesThenRealizesItem_WithoutRemoveReinsert()
    {
        var item  = MakeItem("N1", "Toggle", new VCRect(0, 0, 200, 60));
        var index = new LifecycleDynamicIndex();
        index.Add(item);

        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };
        canvas.Items = index;
        await Flush();

        Assert.NotNull(canvas.VisualFromItem(item));  // realized initially

        // Hide: in-place mutation, no structural change to the index.
        item.IsVisible = false;
        index.RaiseChanged();
        await Flush();

        Assert.Null(canvas.VisualFromItem(item));     // must be virtualized

        // Show: restore and trigger again.
        item.IsVisible = true;
        index.RaiseChanged();
        await Flush();

        Assert.NotNull(canvas.VisualFromItem(item));  // re-realized
    }

    // ── 4. Move (remove → update Bounds → reinsert) ───────────────────────────

    /// <summary>
    /// Moving a projected item uses the remove → update Bounds → reinsert pattern.
    /// The item must remain realized after the move, and VCA must reuse the same
    /// Control instance (no unnecessary re-creation).
    /// <para>
    /// The same-instance assertion is the key DagEdit integration guarantee:
    /// if VCA re-created the Control on every move, animated or frequently-moving
    /// nodes would flicker and destroy focus state.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task MoveItem_SameControlInstance_RepositionedByArrange()
    {
        var item  = MakeItem("N1", "Source", new VCRect(0, 0, 200, 60));
        var index = new LifecycleDynamicIndex();
        index.Add(item);

        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };
        canvas.Items = index;
        await Flush();

        var originalVisual = canvas.VisualFromItem(item);
        Assert.NotNull(originalVisual);

        // Act: simulate a DagEdit node move.
        // Three-step pattern: remove (old bounds) → mutate → reinsert (new bounds).
        index.Remove(item);
        item.Bounds = new VCRect(400, 120, 200, 60);
        index.Add(item);
        index.RaiseChanged();
        await Flush();

        // Item must still be in _visualMap (no re-virtualize on the move).
        var movedVisual = canvas.VisualFromItem(item);
        Assert.NotNull(movedVisual);

        // Critical: VCA must NOT re-create the Control.
        // Same instance = ArrangeOverride repositioned it; no re-realization needed.
        Assert.Same(originalVisual, movedVisual);
        Assert.Equal("Source", (string?)movedVisual.Tag);
    }
}
