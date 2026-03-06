using System.Collections;
using Avalonia.Controls;
using VirtualCanvas.Avalonia.Factories;
using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Avalonia.Tests.Helpers;

/// <summary>Mutable ISpatialItem for tests (ZIndex can be changed to trigger UpdateVisualChildZIndex).</summary>
internal sealed class TestSpatialItem : ISpatialItem
{
    public required VCRect Bounds { get; init; }
    public double Priority { get; init; }
    public int ZIndex { get; set; }       // mutable: setter needed to trigger re-order
    public bool IsVisible { get; init; } = true;
}

/// <summary>Minimal ISpatialIndex backed by a List. Not thread-safe.</summary>
internal sealed class TestSpatialIndex : ISpatialIndex
{
    private readonly List<ISpatialItem> _items = new();

    public event EventHandler? Changed;

    public VCRect Extent => VCRect.Infinite;

    public void AddItem(ISpatialItem item)
    {
        _items.Add(item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // VirtualCanvas only calls GetItemsIntersecting (virtualized) or iterates all (non-virtualized).
    // Return all items for both to keep the helper simple.
    public bool HasItemsIntersecting(VCRect bounds) => _items.Count > 0;
    public IEnumerable<ISpatialItem> GetItemsIntersecting(VCRect bounds) => _items;
    public bool HasItemsInside(VCRect bounds) => _items.Count > 0;
    public IEnumerable<ISpatialItem> GetItemsInside(VCRect bounds) => _items;

    public IEnumerator<ISpatialItem> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>IVisualFactory that creates a new Border for each ISpatialItem.</summary>
internal sealed class ControlFactory : IVisualFactory
{
    public void BeginRealize() { }
    public Control? Realize(ISpatialItem item, bool force) => new Border();
    public bool Virtualize(Control visual) => true;
    public void EndRealize() { }
}
