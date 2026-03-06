using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VirtualCanvas.Avalonia.Factories;
using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Avalonia.DevApp.Demo;

/// <summary>Realizes DemoItems as colored Border controls with an index label.</summary>
internal sealed class DemoVisualFactory : IVisualFactory
{
    private static readonly IBrush[] Palette =
    [
        new SolidColorBrush(Color.FromRgb(0x4E, 0x79, 0xA7)), // blue
        new SolidColorBrush(Color.FromRgb(0xF2, 0x8E, 0x2B)), // orange
        new SolidColorBrush(Color.FromRgb(0xE1, 0x57, 0x59)), // red
        new SolidColorBrush(Color.FromRgb(0x76, 0xB7, 0xB2)), // teal
        new SolidColorBrush(Color.FromRgb(0x59, 0xA1, 0x4F)), // green
        new SolidColorBrush(Color.FromRgb(0xED, 0xC9, 0x48)), // yellow
        new SolidColorBrush(Color.FromRgb(0xB0, 0x7A, 0xA1)), // purple
        new SolidColorBrush(Color.FromRgb(0xFF, 0x9D, 0xA7)), // pink
        new SolidColorBrush(Color.FromRgb(0x9C, 0x75, 0x5F)), // brown
        new SolidColorBrush(Color.FromRgb(0xBA, 0xB0, 0xAC)), // gray
    ];

    internal static readonly IBrush NormalBorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00));
    private static readonly IBrush ForegroundBrush = Brushes.White;

    public void BeginRealize() { }
    public void EndRealize() { }
    public bool Virtualize(Control visual) => true;

    public Control? Realize(ISpatialItem item, bool force)
    {
        if (item is not DemoItem demo) return null;

        return new Border
        {
            Background = Palette[demo.Index % Palette.Length],
            BorderBrush = NormalBorderBrush,
            BorderThickness = new global::Avalonia.Thickness(1),
            CornerRadius = new global::Avalonia.CornerRadius(4),
            Child = new TextBlock
            {
                Text = demo.Index.ToString(),
                Foreground = ForegroundBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            },
            Width = demo.Bounds.Width,
            Height = demo.Bounds.Height,
        };
    }
}
