using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VirtualCanvas.Avalonia.Factories;
using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Avalonia.DevApp.Viewer;

/// <summary>
/// Viewer-only visual factory for the projected-item realization spike.
/// <para>
/// Realizes <see cref="ProjectedNodeItem"/> as a simple Border + TextBlock.
/// Contains zero editor semantics — no click handlers, no selection-aware state,
/// no editor lifecycle hooks. If the item is not a <see cref="ProjectedNodeItem"/>,
/// <c>Realize</c> returns <c>null</c>; VCA will not create a visual for it.
/// </para>
/// <para>
/// This is the first evidence that VCA can act as a pure rendering back-end for
/// a projected, viewer-only scene, independently of any editor control tree.
/// </para>
/// </summary>
internal sealed class ProjectedNodeVisualFactory : IVisualFactory
{
    private static readonly IBrush NodeBackground =
        new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F));   // VS Dark navy

    // Exposed so MainWindow can restore the border brush after deselection.
    internal static readonly IBrush NormalBorderBrush =
        new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));   // VS blue accent

    public void BeginRealize() { }
    public void EndRealize()   { }

    public bool Virtualize(Control visual) => true;

    public Control? Realize(ISpatialItem item, bool force)
    {
        // Type-discriminating: only ProjectedNodeItem is understood by this factory.
        // All other ISpatialItem subtypes (including hypothetical live editor nodes)
        // produce null → VCA skips them, so editor and viewer items cannot accidentally
        // share a visual.
        if (item is not ProjectedNodeItem node)
            return null;

        // VCA pre-filters IsVisible=false before calling Realize, so no check needed here.
        return new Border
        {
            Background       = NodeBackground,
            BorderBrush      = NormalBorderBrush,
            BorderThickness  = new global::Avalonia.Thickness(1),
            CornerRadius     = new global::Avalonia.CornerRadius(4),
            Width            = node.Bounds.Width,
            Height           = node.Bounds.Height,
            Child = new TextBlock
            {
                Text                = node.Label,
                Foreground          = Brushes.White,
                FontSize            = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            },
        };
    }
}
