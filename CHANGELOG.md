# Changelog

All notable changes to VCA (VirtualCanvas.Avalonia) are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

### Changed
- `VirtualCanvas.Core` and `VirtualCanvas.Avalonia` now ship XML IntelliSense documentation
  in their NuGet packages (`GenerateDocumentationFile=true`).
- `VirtualCanvas.Core` NuGet package now includes `README.md` (previously only
  `VirtualCanvas.Avalonia` did).
- CI `verify` workflow: added `dotnet pack` validation step that produces artifacts
  `nupkg-core` and `nupkg-avalonia` on every push/PR to `main`.

---

## [0.1.0-dev] — 2026-03-07

Initial pre-release. Establishes the rendering/virtualization infrastructure
contract. **Not yet stable** — minor version bumps may include breaking changes.

### Added

#### VirtualCanvas.Core
- `VCRect` — framework-agnostic axis-aligned rect (WPF `Rect` semantics;
  `Empty = (+∞,+∞,−∞,−∞)`, `Infinite = (−∞,−∞,+∞,+∞)`).
- `ISpatialItem` — item contract: `Bounds`, `Priority`, `ZIndex`, `IsVisible`.
- `ISpatialIndex` — index contract: `GetItemsIntersecting`, `Changed`, `Extent`, `Any`.
- `PriorityQuadTree<T>` — priority quad-tree port of Microsoft WPF VirtualCanvas.
  Higher `Priority` values returned first. `IntersectsWith` treats boundary contact
  as intersection (≥).
- `SpatialIndex` — concrete `ISpatialIndex` backed by `PriorityQuadTree<ISpatialItem>`.

#### VirtualCanvas.Avalonia
- `VirtualCanvas` control — Avalonia 11 port of the WPF VirtualCanvas.
  Virtualizes `ISpatialItem` children: only viewport-intersecting items are
  realized as Avalonia `Control`s.
  - `StyledProperty`s: `Items`, `Scale`, `Offset`, `IsVirtualizing`,
    `UseRenderTransform`, `SelectedItem`.
  - `DirectProperty`: `ActualViewbox` (read-only).
  - Events: `OffsetChanged`, `ScaleChanged`, `VisualChildrenChanged`,
    `Measuring`, `Measured`, `RealizationCompleted`, `SelectionChanged`.
  - Public helpers: `RealizeItem`, `ForceVirtualizeItem`, `VisualFromItem`,
    `ItemFromVisual`, `GetVisualChildren`, `NotifyOnRealizationCompleted`.
  - Throttled async realization via `Dispatcher.UIThread.Post`
    (self-tuning batch size).
  - `RenderTransform`-based pan/zoom: `Scale × world − offset`.
- `IVisualFactory` — `Realize` / `Virtualize` / `BeginRealize` / `EndRealize`
  contract for consumer-driven rendering.
- `DefaultVisualFactory` — no-op factory (renders nothing; useful for testing).
- `SpatialSelectionChangedEventArgs` — `OldItem` / `NewItem` for `SelectionChanged`.

#### VirtualCanvas.Avalonia.DevApp
- Demo app: 5,000 random `DemoItem`s on a 10,000 × 10,000 world.
- Mouse pan (drag), cursor-centred zoom (scroll wheel), single-item click
  selection with toggle.
- `PerformanceTelemetry` — frame time, pan/zoom/realize rates, UI-thread GC
  allocation via `GC.GetAllocatedBytesForCurrentThread`.
- Two-row status bar: viewport state + telemetry snapshot (1 s interval).

#### Tests
- 36 `VirtualCanvas.Core.Tests` — `PriorityQuadTree` correctness.
- 5 `VirtualCanvas.Avalonia.Tests` (Avalonia headless) — realization lifecycle
  regression tests (bugs A-5.1) and multi-batch boundary test.
- 4 `VirtualCanvas.Avalonia.SmokeTests` — consumer public-API smoke tests.

#### CI
- `.github/workflows/verify.yml` — build + test on push/PR to `main`.
  Coverage collected as Cobertura; TRX and coverage XML uploaded as artifacts.

### Architecture decisions
- `ISpatialItem.OnMeasure(UIElement)` removed (WPF-specific; incompatible with
  framework-agnostic Core).
- `VirtualCanvas` owns selection **state only** (`SelectedItem`); consumers apply
  styles via `SelectionChanged` and re-apply on `RealizationCompleted`.
- `AddVisualChild`/`RemoveVisualChild` → `VisualChildren.Insert/RemoveAt` +
  `LogicalChildren` (reference-based removal after ZIndex reorder).
- `DispatcherOperation.Abort()` → `CancellationTokenSource` + `UIThread.Post`.
- xunit pinned to **2.6.2** in `VirtualCanvas.Avalonia.Tests` (2.9.0 incompatible
  with `Avalonia.Headless.XUnit` 11.0.0 — see `docs/TESTING.md`).

---

[Unreleased]: https://github.com/HeaInSeo/virtualcanvas-avalonia/compare/v0.1.0-dev...HEAD
[0.1.0-dev]: https://github.com/HeaInSeo/virtualcanvas-avalonia/releases/tag/v0.1.0-dev
