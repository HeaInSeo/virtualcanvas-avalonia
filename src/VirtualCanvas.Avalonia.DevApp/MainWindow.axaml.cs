using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using VirtualCanvas.Avalonia.Controls;
using VirtualCanvas.Avalonia.DevApp.Demo;
using VirtualCanvas.Avalonia.DevApp.Telemetry;
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
            b.BorderBrush = DemoVisualFactory.NormalBorderBrush;
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
        string sel = Canvas.SelectedItem is DemoItem d ? $"#{d.Index}" : "none";
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
