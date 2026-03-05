using Avalonia.Controls;
using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Avalonia.Factories;

/// <summary>
/// Creates and destroys Avalonia controls for <see cref="ISpatialItem"/>s.
/// Avalonia 11 port of the WPF IVisualFactory.
/// </summary>
public interface IVisualFactory
{
    /// <summary>Called before a batch of <see cref="Realize"/> calls.</summary>
    void BeginRealize();

    /// <summary>
    /// Creates (or retrieves from cache) an Avalonia <see cref="Control"/> for the given item.
    /// Returns <c>null</c> if the item cannot or should not be realized at this time.
    /// </summary>
    Control? Realize(ISpatialItem item, bool force);

    /// <summary>
    /// Called when a visual is about to be removed from the canvas.
    /// Return <c>true</c> to allow virtualization; <c>false</c> to prevent it (e.g., item has focus).
    /// </summary>
    bool Virtualize(Control visual);

    /// <summary>Called after a batch of <see cref="Realize"/> calls.</summary>
    void EndRealize();
}
