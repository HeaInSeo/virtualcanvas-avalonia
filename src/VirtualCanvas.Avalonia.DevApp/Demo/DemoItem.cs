using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Avalonia.DevApp.Demo;

/// <summary>Sample spatial item: a colored rectangle with an integer index label.</summary>
internal sealed class DemoItem : ISpatialItem
{
    public int Index { get; init; }
    public VCRect Bounds { get; init; }
    public double Priority { get; init; }
    public int ZIndex { get; init; }
    public bool IsVisible { get; init; } = true;
}
