using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VirtualCanvas.Avalonia.Factories;
using VirtualCanvas.Avalonia.Tests.Helpers;
using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;
using Xunit;

using VCCanvas = VirtualCanvas.Avalonia.Controls.VirtualCanvas;

namespace VirtualCanvas.Avalonia.Tests;

// ── file-scoped types: spike-only, not exported beyond this file ──────────────

/// <summary>
/// Minimal projected-item type for realization spike tests.
/// Mimics the DagEdit→VCA hand-off shape (Id, Label, Bounds, …) without
/// referencing any DagEdit assembly.
/// </summary>
file sealed class ProjectedTestItem : ISpatialItem
{
    public required string Id    { get; init; }
    public required string Label { get; init; }
    public VCRect  Bounds   { get; init; }
    public double  Priority { get; init; }
    public int     ZIndex   { get; init; }
    public bool    IsVisible { get; init; } = true;
}

/// <summary>
/// Viewer-only factory under test. Realizes only <see cref="ProjectedTestItem"/>.
/// Counts every Realize call so tests can verify VCA's pre-filtering behaviour.
/// </summary>
file sealed class ProjectedViewerFactory : IVisualFactory
{
    /// <summary>Total number of times Realize was called by VCA.</summary>
    public int RealizeCallCount { get; private set; }

    public void BeginRealize() { }
    public void EndRealize()   { }
    public bool Virtualize(Control visual) => true;

    public Control? Realize(ISpatialItem item, bool force)
    {
        RealizeCallCount++;
        if (item is not ProjectedTestItem proj)
            return null;
        // Tag carries the label so tests can assert identity without inspecting TextBlock.
        return new Border { Tag = proj.Label };
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Phase 1 Viewer PoC spike — projected item realization path.
/// <para>
/// These tests answer the spike's three core questions:
/// <list type="number">
///   <item>Can VCA realize a projected item? (test 1, 4)</item>
///   <item>Is the viewer factory genuinely isolated from unknown item types? (test 2)</item>
///   <item>Does VCA pre-filter IsVisible=false before calling the factory? (test 3)</item>
/// </list>
/// </para>
/// </summary>
public class ProjectedItemRealizationTests
{
    // ── 1. Basic realization path ─────────────────────────────────────────────

    /// <summary>
    /// VCA must call the viewer factory and return the realized Control
    /// for a projected item whose Bounds intersect the active viewport.
    /// The realized Control's Tag must match the item's Label (identity check).
    /// </summary>
    [AvaloniaFact]
    public async Task ProjectedItem_IsRealized_ByViewerFactory()
    {
        var item = new ProjectedTestItem
        {
            Id    = "N1",
            Label = "DataSource",
            Bounds = new VCRect(100, 100, 200, 60),
            ZIndex = 1,
        };

        var index = new TestSpatialIndex();
        index.AddItem(item);

        var factory = new ProjectedViewerFactory();
        var canvas  = new VCCanvas
        {
            VisualFactory  = factory,
            IsVirtualizing = false,   // bypass viewport culling (no layout in headless)
        };

        canvas.Items = index;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var realized = canvas.VisualFromItem(item);
        Assert.NotNull(realized);
        Assert.IsType<Border>(realized);
        Assert.Equal("DataSource", (string?)realized.Tag);
    }

    // ── 2. Type discrimination: factory rejects non-projected items ───────────

    /// <summary>
    /// A <see cref="Helpers.TestSpatialItem"/> is NOT a <c>ProjectedTestItem</c>.
    /// The viewer factory must return null for it; VCA must not create a visual.
    /// <para>
    /// This verifies the key isolation guarantee: even if an editor item and a
    /// projected viewer item share the same ISpatialIndex, the viewer factory will
    /// silently reject the editor item — no control ownership conflict arises.
    /// </para>
    /// VCA does still call Realize (factory.RealizeCallCount == 1), proving
    /// the gate is in the factory, not in VCA itself.
    /// </summary>
    [AvaloniaFact]
    public async Task ViewerFactory_ReturnsNull_ForNonProjectedItem()
    {
        var unknownItem = new TestSpatialItem { Bounds = new VCRect(0, 0, 100, 50) };

        var index = new TestSpatialIndex();
        index.AddItem(unknownItem);

        var factory = new ProjectedViewerFactory();
        var canvas  = new VCCanvas
        {
            VisualFactory  = factory,
            IsVirtualizing = false,
        };

        canvas.Items = index;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.Null(canvas.VisualFromItem(unknownItem));  // no visual
        Assert.Equal(1, factory.RealizeCallCount);        // VCA did call factory; factory rejected
    }

    // ── 3. IsVisible=false — VCA skips factory entirely ──────────────────────

    /// <summary>
    /// VCA pre-filters <c>IsVisible=false</c> items before calling the factory.
    /// <c>factory.RealizeCallCount</c> must remain 0, confirming the guard is in VCA.
    /// </summary>
    [AvaloniaFact]
    public async Task ProjectedItem_NotRealized_WhenIsVisibleFalse()
    {
        var item = new ProjectedTestItem
        {
            Id       = "N-hidden",
            Label    = "Hidden",
            Bounds   = new VCRect(0, 0, 200, 60),
            IsVisible = false,
            ZIndex   = 1,
        };

        var index = new TestSpatialIndex();
        index.AddItem(item);

        var factory = new ProjectedViewerFactory();
        var canvas  = new VCCanvas
        {
            VisualFactory  = factory,
            IsVirtualizing = false,
        };

        canvas.Items = index;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.Null(canvas.VisualFromItem(item));
        Assert.Equal(0, factory.RealizeCallCount);   // VCA never called factory
    }

    // ── 4. Multiple projected items — all realized, labels preserved ──────────

    /// <summary>
    /// A 5-node "pipeline" scene (Source → Filter A → Filter B → Merge → Sink).
    /// Every item must be realized, and each visual's Tag must match its Label,
    /// proving VCA maintains per-item identity across a multi-item viewer scene.
    /// </summary>
    [AvaloniaFact]
    public async Task MultipleProjectedItems_AllRealized_WithCorrectLabels()
    {
        var labels = new[] { "Source", "Filter A", "Filter B", "Merge", "Sink" };
        var items  = labels.Select((label, i) => new ProjectedTestItem
        {
            Id    = $"N{i + 1}",
            Label = label,
            Bounds = new VCRect(i * 250, 100, 200, 60),
            ZIndex = 1,
        }).ToArray();

        var index = new TestSpatialIndex();
        foreach (var item in items) index.AddItem(item);

        var canvas = new VCCanvas
        {
            VisualFactory  = new ProjectedViewerFactory(),
            IsVirtualizing = false,
        };

        canvas.Items = index;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        foreach (var item in items)
        {
            var visual = canvas.VisualFromItem(item);
            Assert.NotNull(visual);
            Assert.Equal(item.Label, (string?)visual.Tag);
        }
    }
}
