using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using VirtualCanvas.Avalonia.Controls;
using VirtualCanvas.Avalonia.DevApp.Demo;
using VirtualCanvas.Avalonia.DevApp.Telemetry;
using VirtualCanvas.Avalonia.DevApp.Viewer;
using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Avalonia.DevApp;

public partial class MainWindow : Window
{
    private const int ItemCount = 5000;
    private const double WorldSize = 10_000.0;
    private const double ItemSize = 80.0;

    // ── Pan state ────────────────────────────────────────────────────
    private Point _panStart;
    private Point _offsetAtPanStart;
    private bool _isPanning;
    private bool _wasLeftPress;

    // ── Selection style constants ─────────────────────────────────────
    // Applied by the consumer (DevApp) in response to Canvas.SelectionChanged.
    private static readonly IBrush SelectionBorderBrush = Brushes.White;
    private static readonly global::Avalonia.Thickness SelectionThickness = new(3);
    private static readonly global::Avalonia.Thickness NormalThickness = new(1);

    // ── Mode ─────────────────────────────────────────────────────────
    private bool _isViewerMode;

    // ── Viewer lifecycle state ────────────────────────────────────────
    // _projectionSource is the DagEdit-side seam simulator.
    // It owns the node set; SyncViewerToCanvas() wires it to VCA.
    private ProjectionSourceHarness? _projectionSource;
    private int                       _nextNodeId = 10;

    // ── Telemetry ────────────────────────────────────────────────────
    private readonly PerformanceTelemetry _telemetry = new();
    private readonly Action<TimeSpan> _animFrameCallback;
    private bool _rafEnabled;

    public MainWindow()
    {
        _animFrameCallback = OnAnimationFrame;

        InitializeComponent();

        Canvas.Items = BuildSpatialIndex();
        Canvas.VisualFactory = new DemoVisualFactory();

        // ── Interaction ───────────────────────────────────────────────
        Canvas.PointerPressed += OnPointerPressed;
        Canvas.PointerMoved += OnPointerMoved;
        Canvas.PointerReleased += OnPointerReleased;
        Canvas.PointerWheelChanged += OnPointerWheelChanged;

        // ── Selection styling — consumer-driven via event ─────────────
        // VirtualCanvas owns the state and fires the event;
        // DevApp applies/removes visual styles here.
        Canvas.SelectionChanged += OnCanvasSelectionChanged;

        // ── Telemetry + re-apply style after re-realization ───────────
        Canvas.RealizationCompleted += (_, _) =>
        {
            _telemetry.RecordRealization();
            // If the selected item was virtualized and just re-realized,
            // its new Border needs the selection highlight re-applied.
            // VirtualCanvas does not do this — it is the consumer's responsibility.
            if (Canvas.SelectedItem != null)
                ApplySelectionStyle(Canvas.SelectedItem, selected: true);
        };

        // ── RAF loop ──────────────────────────────────────────────────
        Opened += (_, _) =>
        {
            _rafEnabled = true;
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_animFrameCallback);
        };
        Closed += (_, _) => _rafEnabled = false;

        // ── 1-second snapshot timer ───────────────────────────────────
        var snapshotTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnSnapshotTick);
        snapshotTimer.Start();
    }

    // ── RAF loop ──────────────────────────────────────────────────────

    private void OnAnimationFrame(TimeSpan ts)
    {
        _telemetry.RecordFrame(ts);
        if (_rafEnabled)
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_animFrameCallback);
    }

    // ── Interaction handlers ──────────────────────────────────────────

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(Canvas).Properties.IsLeftButtonPressed)
        {
            _panStart = e.GetPosition(this);
            _offsetAtPanStart = Canvas.Offset;
            _isPanning = true;
            _wasLeftPress = true;
            e.Pointer.Capture(Canvas);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning) return;
        var pos = e.GetPosition(this);
        Canvas.Offset = new Point(
            _offsetAtPanStart.X - (pos.X - _panStart.X),
            _offsetAtPanStart.Y - (pos.Y - _panStart.Y));
        _telemetry.RecordPan();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool wasLeft = _wasLeftPress;
        _wasLeftPress = false;
        _isPanning = false;
        e.Pointer.Capture(null);

        // Click = left press+release with < 5 px displacement.
        if (wasLeft)
        {
            var pos = e.GetPosition(this);
            double dx = pos.X - _panStart.X;
            double dy = pos.Y - _panStart.Y;
            if (dx * dx + dy * dy < 25.0)
                HandleClick(e.GetPosition(Canvas));
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        _telemetry.RecordZoom();

        double factor = e.Delta.Y > 0 ? 1.12 : 1.0 / 1.12;
        double oldScale = Canvas.Scale;
        double newScale = Math.Clamp(oldScale * factor, 0.02, 50.0);

        var pos = e.GetPosition(Canvas);
        double worldX = (pos.X + Canvas.Offset.X) / oldScale;
        double worldY = (pos.Y + Canvas.Offset.Y) / oldScale;

        Canvas.Scale = newScale;
        Canvas.Offset = new Point(
            worldX * newScale - pos.X,
            worldY * newScale - pos.Y);
    }

    // ── Hit test & selection ──────────────────────────────────────────

    private (double wx, double wy) ViewToWorld(Point viewPt)
    {
        double s = Canvas.Scale;
        return ((viewPt.X + Canvas.Offset.X) / s,
                (viewPt.Y + Canvas.Offset.Y) / s);
    }

    private ISpatialItem? HitTest(Point viewPt)
    {
        if (Canvas.Items == null) return null;

        var (wx, wy) = ViewToWorld(viewPt);
        var queryRect = new VCRect(wx - 0.5, wy - 0.5, 1.0, 1.0);

        ISpatialItem? best = null;
        int bestZ = int.MinValue;

        foreach (var item in Canvas.Items.GetItemsIntersecting(queryRect))
        {
            var b = item.Bounds;
            if (wx >= b.Left && wx <= b.Right && wy >= b.Top && wy <= b.Bottom)
            {
                if (item.ZIndex > bestZ)
                {
                    best = item;
                    bestZ = item.ZIndex;
                }
            }
        }

        return best;
    }

    private void HandleClick(Point viewPt)
    {
        var hit = HitTest(viewPt);
        // Toggle: same item → deselect (null).
        // Setting Canvas.SelectedItem triggers SelectionChanged → OnCanvasSelectionChanged.
        Canvas.SelectedItem = ReferenceEquals(hit, Canvas.SelectedItem) ? null : hit;
        UpdateStatus(Canvas.GetVisualChildren().Count());
    }

    // ── Consumer-driven selection styling ────────────────────────────
    // VirtualCanvas fires SelectionChanged; DevApp owns all style mutations.

    private void OnCanvasSelectionChanged(object? sender, SpatialSelectionChangedEventArgs e)
    {
        ApplySelectionStyle(e.OldItem, selected: false);
        ApplySelectionStyle(e.NewItem, selected: true);
    }

    /// <summary>
    /// Applies or removes the selection highlight on the realized Border for
    /// <paramref name="item"/>. No-op if item is null or not currently realized.
    /// </summary>
    private void ApplySelectionStyle(ISpatialItem? item, bool selected)
    {
        if (item == null) return;
        if (Canvas.VisualFromItem(item) is not Border b) return;

        if (selected)
        {
            b.BorderBrush = SelectionBorderBrush;
            b.BorderThickness = SelectionThickness;
        }
        else
        {
            b.BorderBrush     = _isViewerMode
                ? ProjectedNodeVisualFactory.NormalBorderBrush
                : DemoVisualFactory.NormalBorderBrush;
            b.BorderThickness = NormalThickness;
        }
    }

    // ── 1-second snapshot ─────────────────────────────────────────────

    private void OnSnapshotTick(object? sender, EventArgs e)
    {
        int realized = Canvas.GetVisualChildren().Count();
        _telemetry.Snapshot(realized);
        UpdateStatus(realized);
        UpdateTelemetry();
    }

    // ── Display helpers ───────────────────────────────────────────────

    private void UpdateStatus(int realized)
    {
        var vb = Canvas.ActualViewbox;
        string sel = Canvas.SelectedItem switch
        {
            DemoItem d          => $"Demo #{d.Index}",
            ProjectedNodeItem p => $"Node \"{p.Label}\"",
            _                   => "none",
        };
        StatusText.Text =
            $"Scale: {Canvas.Scale:F2}x  |  " +
            $"Offset: ({Canvas.Offset.X:F0}, {Canvas.Offset.Y:F0})  |  " +
            $"Viewbox: ({vb.X:F0}, {vb.Y:F0}, {vb.Width:F0}×{vb.Height:F0})  |  " +
            $"Realized: {realized}  |  Selected: {sel}";
    }

    private void UpdateTelemetry()
    {
        string delta = _telemetry.RealizedDelta switch
        {
            > 0 => $"+{_telemetry.RealizedDelta}",
            < 0 => $"{_telemetry.RealizedDelta}",
            _ => "0",
        };
        TelemetryText.Text =
            $"Frame: {_telemetry.FrameTimeMs:F1} ms  |  " +
            $"Pan: {_telemetry.PanRate}/s  " +
            $"Zoom: {_telemetry.ZoomRate}/s  |  " +
            $"Realize: {_telemetry.RealizeRate}/s  " +
            $"Δrealized: {delta}  |  " +
            $"Alloc: {_telemetry.AllocKBps:F0} KB/s ~UI";
    }

    // ── Data setup ────────────────────────────────────────────────────

    // ── Mode switching ────────────────────────────────────────────────

    private void OnDemoModeClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => SetDemoMode();

    private void OnViewerModeClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => SetViewerMode();

    private void SetDemoMode()
    {
        _isViewerMode = false;
        Canvas.SelectedItem = null;
        // Disconnect harness before switching away from viewer mode.
        if (_projectionSource != null)
        {
            _projectionSource.ProjectionChanged -= OnProjectionChanged;
            _projectionSource = null;
        }
        Canvas.Scale  = 1.0;
        Canvas.Offset = new Point(0, 0);
        Canvas.VisualFactory = new DemoVisualFactory();
        Canvas.Items = BuildSpatialIndex();
        ModeLabel.Text = "Mode: Demo";
        ViewerControls.IsVisible = false;
    }

    private void SetViewerMode()
    {
        _isViewerMode = true;
        Canvas.SelectedItem = null;
        Canvas.Scale  = 1.0;
        Canvas.Offset = new Point(0, 0);
        Canvas.VisualFactory = new ProjectedNodeVisualFactory();

        // Create harness and wire its change signal to canvas index rebuild.
        // Wiring: source.ProjectionChanged → SyncViewerToCanvas → canvas.Items = snapshot.
        if (_projectionSource != null)
            _projectionSource.ProjectionChanged -= OnProjectionChanged;

        _projectionSource = new ProjectionSourceHarness();
        _projectionSource.ProjectionChanged += OnProjectionChanged;
        _nextNodeId = 10;

        // Populate initial scene via harness.
        // Each Add fires ProjectionChanged → SyncViewerToCanvas → canvas.Items rebuild.
        // Rapid-fire triggers are OK: VCA throttling cancels stale passes; only the final
        // realization pass (after all Adds) actually runs.
        PopulateInitialScene();

        ModeLabel.Text = "Mode: Viewer PoC";
        ViewerControls.IsVisible = true;
    }

    // ── Projection-to-canvas wiring ───────────────────────────────────
    // One handler: source changed → full snapshot rebuild → canvas.Items replaced.
    // This is the "证明用" wiring, NOT the final architecture.
    // Key property: nodes reuse the SAME ProjectedNodeItem object references,
    // so VCA's _visualMap lookup finds them and reuses existing Controls.

    private void OnProjectionChanged(object? sender, EventArgs e)
        => SyncViewerToCanvas();

    private void SyncViewerToCanvas()
    {
        if (_projectionSource == null) return;

        // Full snapshot: create a fresh SpatialIndex from the current harness nodes.
        // Avoids SpatialIndex.Clear()'s internal Changed notification (double-fire).
        var snapshot = new SpatialIndex { Extent = new VCRect(0, 0, 1800, 600) };
        foreach (var n in _projectionSource.Nodes)
            snapshot.Insert(n);

        Canvas.Items = snapshot;  // Items_Changed fires once; throttling handles the rest
    }

    // ── Viewer lifecycle handlers ─────────────────────────────────────
    // Buttons call harness methods → ProjectionChanged → SyncViewerToCanvas → Canvas refresh.

    private void OnViewerAddNode(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_isViewerMode || _projectionSource == null) return;

        int col = _projectionSource.Nodes.Count % 6;
        _projectionSource.Add(new ProjectedNodeItem
        {
            Id    = $"N{_nextNodeId++}",
            Label = $"Node {_nextNodeId - 1}",
            Bounds = new VCRect(60 + col * 260, 360, 200, 60),
            ZIndex = 2,
        });
    }

    private void OnViewerRemoveLast(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_isViewerMode || _projectionSource == null) return;

        var last = _projectionSource.Last;
        if (last == null) return;
        if (ReferenceEquals(Canvas.SelectedItem, last)) Canvas.SelectedItem = null;
        _projectionSource.Remove(last.Id);
    }

    private void OnViewerToggleHide(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_isViewerMode || _projectionSource == null) return;

        // Toggle IsVisible on "Filter B" (N3) or last node.
        // Harness.Move mutates Bounds in-place → same object reference preserved.
        var target = _projectionSource.FindById("N3") ?? _projectionSource.Last;
        if (target == null) return;
        _projectionSource.SetVisible(target.Id, !target.IsVisible);
    }

    private void OnViewerMoveSource(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_isViewerMode || _projectionSource == null) return;

        // Harness.Move mutates Bounds in-place on the same ProjectedNodeItem object.
        // SyncViewerToCanvas rebuilds the index with the same object reference,
        // so VCA's _visualMap still finds the item → Control is REUSED, not re-created.
        var source = _projectionSource.FindById("N1");
        if (source == null) return;

        _projectionSource.Move("N1", new VCRect(
            source.Bounds.X + 100, source.Bounds.Y,
            source.Bounds.Width,   source.Bounds.Height));
    }

    /// <summary>
    /// Populates the initial viewer scene via harness.
    /// Source → [Filter A, Filter B] → Merge → Transform → Sink.
    /// An IsVisible=false sentinel is also added to exercise VCA's pre-filtering.
    /// </summary>
    private void PopulateInitialScene()
    {
        const double NodeW = 200, NodeH = 60, BaseY = 200;

        // Visible pipeline nodes — each Add fires ProjectionChanged.
        foreach (var node in new[]
        {
            new ProjectedNodeItem { Id = "N1", Label = "Source",    Bounds = new VCRect( 60, BaseY,      NodeW, NodeH), ZIndex = 1 },
            new ProjectedNodeItem { Id = "N2", Label = "Filter A",  Bounds = new VCRect(340, BaseY - 80, NodeW, NodeH), ZIndex = 1 },
            new ProjectedNodeItem { Id = "N3", Label = "Filter B",  Bounds = new VCRect(340, BaseY + 80, NodeW, NodeH), ZIndex = 1 },
            new ProjectedNodeItem { Id = "N4", Label = "Merge",     Bounds = new VCRect(620, BaseY,      NodeW, NodeH), ZIndex = 1 },
            new ProjectedNodeItem { Id = "N5", Label = "Transform", Bounds = new VCRect(900, BaseY,      NodeW, NodeH), ZIndex = 1 },
            new ProjectedNodeItem { Id = "N6", Label = "Sink",      Bounds = new VCRect(1180, BaseY,     NodeW, NodeH), ZIndex = 1 },
        })
            _projectionSource!.Add(node);

        // IsVisible=false sentinel: harness tracks it; VCA's pre-filter skips it.
        _projectionSource!.Add(new ProjectedNodeItem
        {
            Id = "N-dropped", Label = "Dropped",
            Bounds = new VCRect(500, 20, NodeW, NodeH), ZIndex = 0, IsVisible = false,
        });
    }

    private static SpatialIndex BuildSpatialIndex()
    {
        var rng = new Random(42);
        var index = new SpatialIndex { Extent = new VCRect(0, 0, WorldSize, WorldSize) };

        for (int i = 0; i < ItemCount; i++)
        {
            double x = rng.NextDouble() * (WorldSize - ItemSize);
            double y = rng.NextDouble() * (WorldSize - ItemSize);
            index.Insert(new DemoItem
            {
                Index = i,
                Bounds = new VCRect(x, y, ItemSize, ItemSize),
                Priority = rng.NextDouble(),
                ZIndex = rng.Next(0, 5),
                IsVisible = true,
            });
        }

        return index;
    }
}
