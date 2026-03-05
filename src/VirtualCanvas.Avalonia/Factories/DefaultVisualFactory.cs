using Avalonia.Controls;
using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Avalonia.Factories;

/// <summary>
/// No-op factory. <see cref="Realize"/> always returns <c>null</c>.
/// Replace with a real implementation to display spatial items.
/// </summary>
internal sealed class DefaultVisualFactory : IVisualFactory
{
    public void BeginRealize() { }
    public Control? Realize(ISpatialItem item, bool force) => null;
    public bool Virtualize(Control visual) => true;
    public void EndRealize() { }
}
