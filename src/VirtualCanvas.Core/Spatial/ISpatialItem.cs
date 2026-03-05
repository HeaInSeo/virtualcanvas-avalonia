using VirtualCanvas.Core.Geometry;

namespace VirtualCanvas.Core.Spatial;

/// <summary>
/// An item that can be located within an ISpatialIndex.
/// </summary>
/// <remarks>
/// WPF reference had <c>OnMeasure(UIElement)</c> — removed. That is a UI layer concern.
/// </remarks>
public interface ISpatialItem
{
    /// <summary>The axis-aligned bounds of this item in canvas coordinates.</summary>
    VCRect Bounds { get; }

    /// <summary>
    /// Rendering priority. Items with <b>higher</b> numeric values are returned first
    /// by <see cref="ISpatialIndex.GetItemsIntersecting"/> (i.e., drawn as background).
    /// <para>
    /// Note: The WPF reference XML comment claimed "lower values first" — that is incorrect.
    /// Actual traversal order (QuadNode circular list, PriorityQueue max-heap) returns
    /// higher numeric values first.
    /// </para>
    /// </summary>
    double Priority { get; }

    /// <summary>
    /// Z-order for rendering. Higher values draw on top.
    /// Distinct from <see cref="Priority"/> which controls realization order.
    /// </summary>
    int ZIndex { get; }

    /// <summary>
    /// When false the item should not be realized even if it intersects the viewport.
    /// </summary>
    bool IsVisible { get; }
}
