using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Avalonia.Controls;

/// <summary>
/// Provides data for the <see cref="VirtualCanvas.SelectionChanged"/> event.
/// </summary>
/// <remarks>
/// <b>Consumer contract:</b> Visual styling in response to selection is the
/// responsibility of the consumer (e.g., DevApp, dagedit). Subscribe to
/// <see cref="VirtualCanvas.SelectionChanged"/> and apply or remove styles
/// via <see cref="VirtualCanvas.VisualFromItem"/> on the old and new items.
/// Re-apply the style on <see cref="VirtualCanvas.RealizationCompleted"/> in
/// case the selected item was virtualized and re-realized.
/// </remarks>
public sealed class SpatialSelectionChangedEventArgs : EventArgs
{
    internal SpatialSelectionChangedEventArgs(ISpatialItem? oldItem, ISpatialItem? newItem)
    {
        OldItem = oldItem;
        NewItem = newItem;
    }

    /// <summary>The previously selected item, or <c>null</c> if nothing was selected.</summary>
    public ISpatialItem? OldItem { get; }

    /// <summary>The newly selected item, or <c>null</c> if the selection was cleared.</summary>
    public ISpatialItem? NewItem { get; }
}
