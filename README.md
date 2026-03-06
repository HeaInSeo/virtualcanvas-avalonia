# VirtualCanvas.Avalonia

Avalonia 11 port of the [Microsoft WPF VirtualCanvas](https://github.com/microsoft/VirtualCanvas) control.

Renders hundreds of thousands of spatial items on an infinite, pannable, zoomable canvas by **virtualizing** children: only items whose world-space bounds intersect the current viewport are realized as Avalonia controls. The rest are kept as lightweight data in a priority quad-tree.

---

## Features

| Feature | Status |
|---------|--------|
| Virtualization (realize / virtualize by viewport) | ✅ |
| Throttled async realization (self-tuning batch size) | ✅ |
| Scale / Offset pan-zoom with RenderTransform | ✅ |
| `ISpatialIndex` / `ISpatialItem` abstraction | ✅ |
| `PriorityQuadTree<T>` spatial index | ✅ |
| `SpatialIndex` concrete adapter | ✅ |
| Consumer-driven selection via `SelectionChanged` event | ✅ |
| DevApp with pan, zoom, hit-test selection, telemetry | ✅ |
| CI (dotnet build + test) | ✅ |

---

## Project Structure

```
src/
  VirtualCanvas.Core/            Pure C#, no UI dependency
    Geometry/VCRect.cs           Axis-aligned rect (WPF Rect semantics)
    Spatial/ISpatialItem.cs      Item contract (Bounds, Priority, ZIndex, IsVisible)
    Spatial/ISpatialIndex.cs     Index contract (GetItemsIntersecting, Changed, …)
    Spatial/SpatialIndex.cs      Concrete ISpatialIndex backed by PriorityQuadTree
    Spatial/PriorityQuadTree/    Priority quad-tree (port of Microsoft original)

  VirtualCanvas.Avalonia/        Avalonia 11 UI library
    Controls/VirtualCanvas.cs    Main control (layout, virtualization, pan/zoom)
    Controls/VirtualCanvas.Throttling.cs  Async batch realization
    Controls/SpatialSelectionChangedEventArgs.cs
    Factories/IVisualFactory.cs  Realize / Virtualize contract for consumers
    Factories/DefaultVisualFactory.cs

  VirtualCanvas.Avalonia.DevApp/ Validation app (Exe)
    Demo/DemoItem.cs             Sample ISpatialItem
    Demo/DemoVisualFactory.cs    Colored Border factory
    Telemetry/PerformanceTelemetry.cs  Frame time, event rates, GC alloc

tests/
  VirtualCanvas.Core.Tests/      xunit 2.9.0
  VirtualCanvas.Avalonia.Tests/  xunit 2.6.2 + Avalonia.Headless.XUnit 11.0.0
```

---

## Requirements

- .NET 8.0 SDK
- Avalonia 11.0.0

---

## Build & Test

```bash
dotnet build
dotnet test
```

All 41 tests should pass (36 Core + 5 Avalonia headless).

## Run DevApp

```bash
dotnet run --project src/VirtualCanvas.Avalonia.DevApp
```

- **Drag** to pan
- **Scroll** to zoom (cursor-centered)
- **Click** to select an item (toggle)

The status bar shows Scale, Offset, Viewbox, Realized count, Selected item, and performance telemetry (frame time, event rates, GC allocation on UI thread).

---

## Architecture

### Virtualization

```
VirtualCanvas.ArrangeOverride
  └─ BeginUpdate()
       └─ RealizeCoreWithThrottling()          (Dispatcher.UIThread.Post)
            └─ RealizeOverride()               (coroutine / IEnumerator)
                 ├─ Items.GetItemsIntersecting(ActualViewbox)  → realize batch
                 └─ _visualMap \ visible set                   → virtualize batch
```

Items outside the viewport (`ActualViewbox`) are kept as `ISpatialItem` data only. When they enter the viewport, `IVisualFactory.Realize` creates an Avalonia `Control`; when they leave, `IVisualFactory.Virtualize` removes it.

### Coordinate System

```
screen_x = world_x × scale − offset_x
world_x  = (screen_x + offset_x) / scale
```

`Scale` and `Offset` are `StyledProperty`s; changing them updates `ActualViewbox` and triggers re-virtualization.

### Selection Contract

`VirtualCanvas` owns selection **state** (`SelectedItem` `StyledProperty`) and fires `SelectionChanged`. It **never** mutates child control styles. Consumers apply styles in the event handler and re-apply them on `RealizationCompleted` (in case the item was virtualized and re-realized).

```csharp
canvas.SelectionChanged += (_, e) =>
{
    Unstyle(e.OldItem);
    Style(e.NewItem);
};

canvas.RealizationCompleted += (_, _) =>
{
    if (canvas.SelectedItem != null)
        Style(canvas.SelectedItem);   // re-apply after re-realize
};
```

### IVisualFactory

Implement `IVisualFactory` to control how `ISpatialItem`s become Avalonia controls:

```csharp
public interface IVisualFactory
{
    void BeginRealize();
    Control? Realize(ISpatialItem item, bool force);
    bool Virtualize(Control visual);   // return false to keep (e.g., focused)
    void EndRealize();
}
```

Assign via `canvas.VisualFactory = new MyFactory();`.

---

## xUnit Version Pin

`tests/VirtualCanvas.Avalonia.Tests` is pinned to **xunit 2.6.2** (not 2.9.0).
See [`docs/TESTING.md`](docs/TESTING.md) for the full explanation.

---

## WPF Reference

The original WPF implementation lives in `VirtualCanvas-ref/` (read-only reference, not compiled).
Key divergences from WPF:

| WPF | Avalonia port |
|-----|---------------|
| `AddVisualChild` / `RemoveVisualChild` | `VisualChildren.Insert/RemoveAt` + `LogicalChildren` |
| `DispatcherOperation.Abort()` | `CancellationTokenSource` + `Dispatcher.UIThread.Post` |
| `DependencyProperty` | `StyledProperty<T>` / `DirectProperty<TOwner,T>` |
| `System.Windows.Rect` | `VCRect` (self-contained, no UI dependency) |
| `ISpatialItem.OnMeasure(UIElement)` | removed (WPF-specific) |

---

## License

MIT — see [LICENSE](LICENSE) (original WPF VirtualCanvas: © Microsoft Corporation, MIT).
