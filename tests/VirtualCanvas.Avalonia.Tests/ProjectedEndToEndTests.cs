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

// ── file-scoped test harness ──────────────────────────────────────────────────
// Reuses LifecycleMutableItem / LifecycleDynamicIndex from ProjectedLifecycleTests.cs
// (internal: accessible across the test assembly).

/// <summary>
/// Minimal projection source harness — test-internal mirror of DevApp's
/// <c>ProjectionSourceHarness</c>. Works with <see cref="LifecycleMutableItem"/>
/// (the test-assembly equivalent of <c>ProjectedNodeItem</c>).
/// <para>
/// This isolation ensures end-to-end tests remain fully self-contained and do not
/// reference any DevApp assembly type.
/// </para>
/// </summary>
internal sealed class E2EProjectionSource
{
    private readonly List<LifecycleMutableItem> _nodes = new();

    public event EventHandler? ProjectionChanged;
    public IReadOnlyList<LifecycleMutableItem> Nodes => _nodes;

    public LifecycleMutableItem? Last      => _nodes.Count > 0 ? _nodes[^1] : null;
    public LifecycleMutableItem? Find(string id) => _nodes.Find(x => x.Id == id);

    public void Add(LifecycleMutableItem node) { _nodes.Add(node); Notify(); }

    public void Remove(string id)
    {
        if (_nodes.RemoveAll(x => x.Id == id) > 0) Notify();
    }

    public void Move(string id, VCRect newBounds)
    {
        var n = _nodes.Find(x => x.Id == id);
        if (n != null) { n.Bounds = newBounds; Notify(); }
    }

    public void SetVisible(string id, bool visible)
    {
        var n = _nodes.Find(x => x.Id == id);
        if (n != null) { n.IsVisible = visible; Notify(); }
    }

    private void Notify() => ProjectionChanged?.Invoke(this, EventArgs.Empty);
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Phase E-2 end-to-end spike: verifies the full projection trigger → index rebuild →
/// VCA viewer refresh loop is closed.
/// <para>
/// Wiring pattern (pseudocode):
/// <code>
///   source.ProjectionChanged
///     → snapshot = new SpatialIndex(source.Nodes)
///     → canvas.Items = snapshot          // Items_Changed → InvalidateReality
///     → VCA realizes / virtualizes items
/// </code>
/// </para>
/// <para>
/// Each test proves one segment of the end-to-end path:
/// <list type="number">
///   <item>Add  — projection trigger causes new item to appear in viewer.</item>
///   <item>Remove — projection trigger removes item from viewer.</item>
///   <item>Move  — same Control reused despite full snapshot rebuild.</item>
///   <item>Hide  — IsVisible=false virtualizes without structural index change.</item>
/// </list>
/// </para>
/// </summary>
public class ProjectedEndToEndTests
{
    // Flush all Input-priority dispatcher work (realization batches) before asserting.
    private static DispatcherOperation Flush()
        => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private static LifecycleMutableItem MakeItem(string id, string label, VCRect bounds)
        => new() { Id = id, Label = label, Bounds = bounds, ZIndex = 1 };

    /// <summary>
    /// Wires <paramref name="source"/>.ProjectionChanged → full snapshot rebuild →
    /// <paramref name="canvas"/>.Items assignment.
    /// <para>
    /// This is the "证明用" wiring: a new SpatialIndex is created on every change.
    /// Nodes reuse the SAME <see cref="LifecycleMutableItem"/> object references,
    /// so VCA's _visualMap can find them and reuse Controls across rebuilds.
    /// This is NOT the final batching architecture; it is the minimum proof.
    /// </para>
    /// </summary>
    private static void Wire(E2EProjectionSource source, VCCanvas canvas)
    {
        source.ProjectionChanged += (_, _) =>
        {
            // Full snapshot: new index, same object references.
            // Avoids SpatialIndex.Clear()'s internal Changed double-fire.
            // Extent must be finite: PriorityQuadTree rejects ±Infinity coordinates.
            var snapshot = new SpatialIndex { Extent = new VCRect(-500, -500, 3000, 2000) };
            foreach (var n in source.Nodes)
                snapshot.Insert(n);

            // canvas.Items = snapshot fires Items_Changed synchronously,
            // which schedules the async realization pass (Input priority).
            canvas.Items = snapshot;
        };
    }

    // ── 1. Add ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A projection source change (Add) must cause the new item to be realized
    /// in the viewer. Verifies the full trigger → wiring → realize path is closed.
    /// </summary>
    [AvaloniaFact]
    public async Task EndToEnd_Add_ProjectionTrigger_IsRealized()
    {
        var source = new E2EProjectionSource();
        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };
        Wire(source, canvas);

        var item = MakeItem("N1", "Source", new VCRect(0, 0, 200, 60));
        source.Add(item);   // → ProjectionChanged → Wire → canvas.Items = snapshot{item}
        await Flush();

        var realized = canvas.VisualFromItem(item);
        Assert.NotNull(realized);
        Assert.Equal("Source", (string?)realized.Tag);
    }

    // ── 2. Remove ────────────────────────────────────────────────────────────

    /// <summary>
    /// A projection source change (Remove) must virtualize the removed item.
    /// Items remaining in the source must stay realized.
    /// </summary>
    [AvaloniaFact]
    public async Task EndToEnd_Remove_ProjectionTrigger_IsVirtualized()
    {
        var source = new E2EProjectionSource();
        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };
        Wire(source, canvas);

        var keep   = MakeItem("N1", "Keep",   new VCRect(  0, 0, 200, 60));
        var remove = MakeItem("N2", "Remove", new VCRect(260, 0, 200, 60));
        source.Add(keep);
        source.Add(remove);
        await Flush();

        Assert.NotNull(canvas.VisualFromItem(keep));
        Assert.NotNull(canvas.VisualFromItem(remove));

        // Act: projection source removes one node.
        source.Remove("N2");   // → ProjectionChanged → snapshot{keep only} → canvas.Items
        await Flush();

        Assert.NotNull(canvas.VisualFromItem(keep));     // still realized
        Assert.Null(canvas.VisualFromItem(remove));      // virtualized
    }

    // ── 3. Move (same-object-reference → Control reuse) ──────────────────────

    /// <summary>
    /// A projection source change (Move) must reuse the same Control instance.
    /// <para>
    /// Key observation: <see cref="E2EProjectionSource.Move"/> mutates
    /// <see cref="LifecycleMutableItem.Bounds"/> in-place, so the snapshot rebuild
    /// inserts the SAME object reference. VCA finds it in _visualMap → no re-realization.
    /// </para>
    /// <para>
    /// If the source had created a NEW item object with new Bounds, VCA would
    /// re-realize (new Control). That re-creation path is NOT tested here.
    /// Documenting this as an unresolved design decision.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task EndToEnd_Move_ControlReused_ViaSameObjectReference()
    {
        var source = new E2EProjectionSource();
        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };
        Wire(source, canvas);

        var item = MakeItem("N1", "Source", new VCRect(0, 0, 200, 60));
        source.Add(item);
        await Flush();

        var originalVisual = canvas.VisualFromItem(item);
        Assert.NotNull(originalVisual);

        // Act: projection source moves the node.
        source.Move("N1", new VCRect(400, 120, 200, 60));
        // → ProjectionChanged
        // → snapshot: same item object, item.Bounds = (400,120,200,60)
        // → canvas.Items = snapshot
        // → RealizeItem: item in _visualMap → returns existing Control (REUSED)
        // → ArrangeOverride: repositions Control at new Bounds.X/Y
        await Flush();

        var movedVisual = canvas.VisualFromItem(item);
        Assert.NotNull(movedVisual);

        // Critical assertion: same Control instance across snapshot rebuild.
        // If this fails, it means object references were replaced (not mutated)
        // → every move would re-create the Control (animation/focus state lost).
        Assert.Same(originalVisual, movedVisual);
        Assert.Equal("Source", (string?)movedVisual.Tag);
    }

    // ── 4. Hide / Show (IsVisible toggle) ────────────────────────────────────

    /// <summary>
    /// A projection source change (SetVisible=false then true) must virtualize
    /// then re-realize the item, without structural index changes.
    /// <para>
    /// VCA pre-filters <c>IsVisible=false</c> items before calling Realize,
    /// and <c>ShouldVirtualize</c> returns <c>true</c> for them. The snapshot
    /// rebuild still contains the item (it is NOT removed from the source);
    /// visibility state alone drives the realize/virtualize decision.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task EndToEnd_HideThenShow_VirtualizesAndRealizes()
    {
        var source = new E2EProjectionSource();
        var canvas = new VCCanvas { VisualFactory = new LifecycleViewerFactory(), IsVirtualizing = false };
        Wire(source, canvas);

        var item = MakeItem("N1", "Toggle", new VCRect(0, 0, 200, 60));
        source.Add(item);
        await Flush();

        Assert.NotNull(canvas.VisualFromItem(item));  // initially realized

        // Hide.
        source.SetVisible("N1", false);
        // → item.IsVisible = false → ProjectionChanged → snapshot{item (hidden)}
        // → canvas.Items = snapshot → RealizeItem returns null → ShouldVirtualize → true
        await Flush();

        Assert.Null(canvas.VisualFromItem(item));     // virtualized

        // Show.
        source.SetVisible("N1", true);
        await Flush();

        Assert.NotNull(canvas.VisualFromItem(item));  // re-realized
    }
}
