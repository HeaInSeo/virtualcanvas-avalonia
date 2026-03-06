using Avalonia.Controls;
using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Avalonia.Factories;

/// <summary>
/// Creates and destroys Avalonia controls for <see cref="ISpatialItem"/>s.
/// Avalonia 11 port of the WPF IVisualFactory.
/// </summary>
public interface IVisualFactory
{
    /// <summary>
    /// Called before a batch of <see cref="Realize"/> calls.
    /// <para>
    /// <b>Nesting contract:</b> <c>BeginRealize</c>/<c>EndRealize</c> may be called in
    /// nested pairs — once for the overall realization pass and once per throttled batch
    /// within it. This mirrors the WPF reference implementation. Stateful factories must
    /// handle nested calls idempotently (e.g., ref-count or ignore if already open).
    /// </para>
    /// <para>
    /// <b>Resource scope:</b> <c>BeginRealize</c>/<c>EndRealize</c> delimit a logical
    /// resource scope (e.g., open a shared cache or transaction). Implementations must
    /// not rely on the exact call count; only the outermost scope matters.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Called after a batch of <see cref="Realize"/> calls.
    /// Paired with <see cref="BeginRealize"/>; see its nesting and resource-scope contract.
    /// </summary>
    void EndRealize();
}
