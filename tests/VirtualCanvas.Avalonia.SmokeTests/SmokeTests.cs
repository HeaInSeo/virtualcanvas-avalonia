// Consumer smoke tests: use ONLY the public API of VirtualCanvas.Avalonia.
// No shared internal helpers — this simulates an external library consumer.

using Avalonia.Headless.XUnit;
using VirtualCanvas.Avalonia.Controls;
using VirtualCanvas.Avalonia.Factories;
using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;
using Xunit;

namespace VirtualCanvas.Avalonia.SmokeTests;

// ── Minimal ISpatialItem implementation (no shared test helpers) ──────────────

file sealed class SmokeItem : ISpatialItem
{
    public VCRect Bounds { get; init; }
    public double Priority { get; init; }
    public int ZIndex { get; init; }
    public bool IsVisible { get; init; } = true;
}

// ── Smoke tests ───────────────────────────────────────────────────────────────

public sealed class SmokeTests
{
    [AvaloniaFact]
    public void CanInstantiateVirtualCanvas()
    {
        var canvas = new VirtualCanvas.Avalonia.Controls.VirtualCanvas();
        Assert.NotNull(canvas);
    }

    [AvaloniaFact]
    public void CanAssignItemsAndFactory()
    {
        var canvas = new VirtualCanvas.Avalonia.Controls.VirtualCanvas();
        var index = new SpatialIndex { Extent = new VCRect(0, 0, 1000, 1000) };
        index.Insert(new SmokeItem { Bounds = new VCRect(10, 10, 50, 50) });

        canvas.Items = index;
        canvas.VisualFactory = new DefaultVisualFactory();

        Assert.Same(index, canvas.Items);
        Assert.IsType<DefaultVisualFactory>(canvas.VisualFactory);
    }

    [AvaloniaFact]
    public void SelectionChangedEventFires()
    {
        var canvas = new VirtualCanvas.Avalonia.Controls.VirtualCanvas();
        var index = new SpatialIndex { Extent = new VCRect(0, 0, 1000, 1000) };
        var item = new SmokeItem { Bounds = new VCRect(10, 10, 50, 50) };
        index.Insert(item);
        canvas.Items = index;

        SpatialSelectionChangedEventArgs? captured = null;
        canvas.SelectionChanged += (_, e) => captured = e;

        canvas.SelectedItem = item;

        Assert.NotNull(captured);
        Assert.Null(captured.OldItem);
        Assert.Same(item, captured.NewItem);
    }

    [AvaloniaFact]
    public void SelectionClearedFiresWithNullNewItem()
    {
        var canvas = new VirtualCanvas.Avalonia.Controls.VirtualCanvas();
        var item = new SmokeItem { Bounds = new VCRect(0, 0, 10, 10) };
        canvas.SelectedItem = item;

        SpatialSelectionChangedEventArgs? captured = null;
        canvas.SelectionChanged += (_, e) => captured = e;

        canvas.SelectedItem = null;

        Assert.NotNull(captured);
        Assert.Same(item, captured.OldItem);
        Assert.Null(captured.NewItem);
    }
}
