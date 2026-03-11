using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using VirtualCanvas.Avalonia.Factories;
using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Avalonia.Controls;

/// <summary>
/// An infinite canvas that virtualizes <see cref="ISpatialItem"/> children using an
/// <see cref="ISpatialIndex"/> and a user-supplied <see cref="IVisualFactory"/>.
/// Avalonia 11 port of the WPF VirtualCanvas control.
/// </summary>
public partial class VirtualCanvas : Control
{
    #region StyledProperties

    public static readonly StyledProperty<ISpatialIndex?> ItemsProperty =
        AvaloniaProperty.Register<VirtualCanvas, ISpatialIndex?>(nameof(Items));

    public static readonly StyledProperty<bool> IsVirtualizingProperty =
        AvaloniaProperty.Register<VirtualCanvas, bool>(nameof(IsVirtualizing), defaultValue: true);

    public static readonly StyledProperty<bool> UseRenderTransformProperty =
        AvaloniaProperty.Register<VirtualCanvas, bool>(nameof(UseRenderTransform), defaultValue: true);

    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<VirtualCanvas, double>(nameof(Scale), defaultValue: 1.0);

    public static readonly StyledProperty<Point> OffsetProperty =
        AvaloniaProperty.Register<VirtualCanvas, Point>(nameof(Offset));

    /// <summary>
    /// The currently selected spatial item, or <c>null</c> if nothing is selected.
    /// Set this property (e.g., from a hit-test result) to change the selection;
    /// the <see cref="SelectionChanged"/> event will fire and the consumer is
    /// responsible for updating visual styles.
    /// </summary>
    public static readonly StyledProperty<ISpatialItem?> SelectedItemProperty =
        AvaloniaProperty.Register<VirtualCanvas, ISpatialItem?>(nameof(SelectedItem));

    private VCRect _actualViewbox = VCRect.Empty;
    public static readonly DirectProperty<VirtualCanvas, VCRect> ActualViewboxProperty =
        AvaloniaProperty.RegisterDirect<VirtualCanvas, VCRect>(
            nameof(ActualViewbox),
            o => o.ActualViewbox);

    #endregion

    #region CLR Properties

    public ISpatialIndex? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public bool IsVirtualizing
    {
        get => GetValue(IsVirtualizingProperty);
        set => SetValue(IsVirtualizingProperty, value);
    }

    public bool UseRenderTransform
    {
        get => GetValue(UseRenderTransformProperty);
        set => SetValue(UseRenderTransformProperty, value);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public Point Offset
    {
        get => GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    /// <inheritdoc cref="SelectedItemProperty"/>
    public ISpatialItem? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public VCRect ActualViewbox => _actualViewbox;

    #endregion

    #region Events

    public event EventHandler? OffsetChanged;
    public event EventHandler? ScaleChanged;
    public event EventHandler? VisualChildrenChanged;
    public event EventHandler? Measuring;
    public event EventHandler? Measured;

    /// <summary>
    /// Raised when <see cref="SelectedItem"/> changes.
    /// <para>
    /// <b>Consumer contract:</b> The consumer (DevApp, dagedit, etc.) is responsible
    /// for applying or removing visual styles in response to this event.
    /// VirtualCanvas does not modify any child control's appearance.
    /// Use <see cref="VisualFromItem"/> to obtain the realized control for styling.
    /// </para>
    /// </summary>
    public event EventHandler<SpatialSelectionChangedEventArgs>? SelectionChanged;

    #endregion

    #region Static Constructor

    static VirtualCanvas()
    {
        ItemsProperty.Changed.AddClassHandler<VirtualCanvas>((c, e) => c.OnItemsPropertyChanged(e));
        ScaleProperty.Changed.AddClassHandler<VirtualCanvas>((c, e) => c.OnScalePropertyChanged(e));
        OffsetProperty.Changed.AddClassHandler<VirtualCanvas>((c, e) => c.OnOffsetPropertyChanged(e));
        IsVirtualizingProperty.Changed.AddClassHandler<VirtualCanvas>((c, _) => c.InvalidateReality());
        UseRenderTransformProperty.Changed.AddClassHandler<VirtualCanvas>((c, _) => c.OnUseRenderTransformChanged());
        BoundsProperty.Changed.AddClassHandler<VirtualCanvas>((c, _) => c.UpdateActualViewbox());
        SelectedItemProperty.Changed.AddClassHandler<VirtualCanvas>((c, e) => c.OnSelectedItemPropertyChanged(e));
    }

    #endregion

    #region Fields & Initialization

    // Sorted ascending by ZIndex (lower = rendered first = background).
    // Always kept in sync with VisualChildren.
    private readonly List<(Control Visual, int ZIndex)> _sortedVisuals = new();
    private readonly Dictionary<ISpatialItem, Control> _visualMap = new();
    private readonly Dictionary<Control, ISpatialItem> _itemMap = new();

    // Pinned items are exempt from normal virtualization (ShouldVirtualize returns false).
    // Identity is reference-based, matching _visualMap.
    // Teardown (Items==null) and explicit hide (!IsVisible) still override pinning.
    // ForceVirtualizeItem bypasses pinning by design.
    private readonly HashSet<ISpatialItem> _pinnedItems = new();

    private IVisualFactory _factory;
    private readonly IVisualFactory _defaultFactory;

    private bool _doingLayout;

    private ScaleTransform? _appliedScaleTransform;
    private TranslateTransform? _appliedTranslateTransform;

    /// <summary>Determines whether child outline geometries are computed after Measure.</summary>
    public bool ComputeOutlineGeometry { get; set; } = true;

    public VirtualCanvas()
    {
        _defaultFactory = new DefaultVisualFactory();
        _factory = _defaultFactory;
        UpdateActualViewbox();
        UpdateRenderTransform();
    }

    /// <summary>
    /// The factory used to realize/virtualize controls.
    /// Assigning <c>null</c> reverts to the internal no-op default factory.
    /// </summary>
    public IVisualFactory VisualFactory
    {
        get => _factory;
        set => _factory = value ?? _defaultFactory;
    }

    #endregion

    #region Property Change Handlers

    private void OnItemsPropertyChanged(AvaloniaPropertyChangedEventArgs e)
        => OnItemsChanged(e.OldValue as ISpatialIndex, e.NewValue as ISpatialIndex);

    protected virtual void OnItemsChanged(ISpatialIndex? oldItems, ISpatialIndex? newItems)
    {
        if (oldItems != null) oldItems.Changed -= Items_Changed;
        if (newItems != null) newItems.Changed += Items_Changed;
        Items_Changed(this, EventArgs.Empty);
    }

    private void Items_Changed(object? sender, EventArgs e)
    {
        if (_doingLayout) return;

        // If index is now empty, immediately remove all realized visuals.
        // Pinned items are skipped: they remain realized even with an empty snapshot.
        // factory.Virtualize is called first (I-1) so pool cleanup is always notified.
        if (Items != null && !Items.Any())
        {
            foreach (var pair in _visualMap.ToList())
            {
                if (_pinnedItems.Contains(pair.Key)) continue;
                _factory.Virtualize(pair.Value);
                ForceVirtualizeItem(pair.Key, pair.Value);
            }
        }

        InvalidateReality();
        InvalidateMeasure();
        InvalidateArrange();
    }

    private void OnScalePropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        double scale = (double)e.NewValue!;
        UpdateActualViewbox();
        ScaleOverride(scale);
        OnScaleChanged();
    }

    protected virtual void OnScaleChanged() => ScaleChanged?.Invoke(this, EventArgs.Empty);

    private void OnOffsetPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        Point offset = (Point)e.NewValue!;
        UpdateActualViewbox();
        OffsetOverride(offset);
        OnOffsetChanged();
    }

    protected virtual void OnOffsetChanged()
    {
        InvalidateReality();
        OffsetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnUseRenderTransformChanged() => UpdateRenderTransform();

    private void OnSelectedItemPropertyChanged(AvaloniaPropertyChangedEventArgs e)
        => OnSelectionChanged(e.OldValue as ISpatialItem, e.NewValue as ISpatialItem);

    /// <summary>Fires <see cref="SelectionChanged"/>. Override to add subclass logic.</summary>
    protected virtual void OnSelectionChanged(ISpatialItem? oldItem, ISpatialItem? newItem)
        => SelectionChanged?.Invoke(this, new SpatialSelectionChangedEventArgs(oldItem, newItem));

    #endregion

    #region ActualViewbox

    private void UpdateActualViewbox()
    {
        double scale = Scale;
        if (scale <= 0) return;
        Point offset = Offset;
        Size size = Bounds.Size;
        var newViewbox = new VCRect(
            offset.X / scale, offset.Y / scale,
            size.Width / scale, size.Height / scale);
        SetAndRaise(ActualViewboxProperty, ref _actualViewbox, newViewbox);
    }

    private void UpdateActualViewboxWithSize(Size size)
    {
        double scale = Scale;
        if (scale <= 0) return;
        Point offset = Offset;
        var newViewbox = new VCRect(
            offset.X / scale, offset.Y / scale,
            size.Width / scale, size.Height / scale);
        SetAndRaise(ActualViewboxProperty, ref _actualViewbox, newViewbox);
    }

    #endregion

    #region Transform Management

    private void UpdateRenderTransform()
    {
        if (UseRenderTransform)
        {
            _appliedScaleTransform = new ScaleTransform(Scale, Scale);
            _appliedTranslateTransform = new TranslateTransform(-Offset.X, -Offset.Y);
            var group = new TransformGroup();
            group.Children.Add(_appliedScaleTransform);
            group.Children.Add(_appliedTranslateTransform);
            RenderTransform = group;
        }
        else
        {
            _appliedScaleTransform = null;
            _appliedTranslateTransform = null;
            RenderTransform = null;
        }
    }

    protected virtual void ScaleOverride(double scale)
    {
        if (_appliedScaleTransform != null)
        {
            _appliedScaleTransform.ScaleX = scale;
            _appliedScaleTransform.ScaleY = scale;
        }
        else
        {
            InvalidateArrange();
        }
    }

    protected virtual void OffsetOverride(Point offset)
    {
        if (_appliedTranslateTransform != null)
        {
            _appliedTranslateTransform.X = -offset.X;
            _appliedTranslateTransform.Y = -offset.Y;
        }
        else
        {
            InvalidateArrange();
        }
    }

    #endregion

    #region Visual Children Management

    private int FindInsertIndex(int zIndex)
    {
        int lo = 0, hi = _sortedVisuals.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_sortedVisuals[mid].ZIndex <= zIndex) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private void AddVisualChildInternal(Control visual, int zIndex)
    {
        int idx = FindInsertIndex(zIndex);
        _sortedVisuals.Insert(idx, (visual, zIndex));
        VisualChildren.Insert(idx, visual);
        LogicalChildren.Insert(idx, visual);
    }

    private void RemoveVisualChildInternal(Control visual)
    {
        int idx = _sortedVisuals.FindIndex(x => ReferenceEquals(x.Visual, visual));
        if (idx >= 0)
        {
            _sortedVisuals.RemoveAt(idx);
            VisualChildren.RemoveAt(idx);
            // Reference-based removal: LogicalChildren may be out of order with
            // _sortedVisuals after a ZIndex reorder (see UpdateVisualChildZIndex),
            // so index-based RemoveAt would remove the wrong element.
            LogicalChildren.Remove(visual);
        }
    }

    private bool UpdateVisualChildZIndex(Control visual, int newZIndex)
    {
        int oldIdx = _sortedVisuals.FindIndex(x => ReferenceEquals(x.Visual, visual));
        if (oldIdx < 0) return false;
        if (_sortedVisuals[oldIdx].ZIndex == newZIndex) return false;

        _sortedVisuals.RemoveAt(oldIdx);
        VisualChildren.RemoveAt(oldIdx);
        // LogicalChildren is intentionally NOT reordered here.
        // Its ordering may diverge from _sortedVisuals/VisualChildren after this call.
        // Removal always uses reference-based lookup (see RemoveVisualChildInternal).

        int newIdx = FindInsertIndex(newZIndex);
        _sortedVisuals.Insert(newIdx, (visual, newZIndex));
        VisualChildren.Insert(newIdx, visual);
        return true;
    }

    private void ClearAllVisualChildren()
    {
        _sortedVisuals.Clear();
        VisualChildren.Clear();
        LogicalChildren.Clear();
    }

    /// <summary>Returns the currently realized controls in ascending ZIndex order.</summary>
    public IEnumerable<Control> GetVisualChildren()
        => _sortedVisuals.Select(x => x.Visual);

    #endregion

    #region Virtualization

    /// <summary>Synchronously realizes every item in <paramref name="itemsToRealize"/>.</summary>
    public void RealizeItems(IEnumerable<ISpatialItem> itemsToRealize)
    {
        foreach (var item in itemsToRealize)
            if (!_visualMap.ContainsKey(item))
                RealizeItem(item, false);
    }

    /// <summary>Realizes a single item, creating its visual child if necessary.</summary>
    public Control? RealizeItem(ISpatialItem item) => RealizeItem(item, true);

    internal Control? RealizeItem(ISpatialItem item, bool force)
    {
        if (!item.IsVisible) return null;

        if (!_visualMap.TryGetValue(item, out var visual))
        {
            visual = _factory.Realize(item, force);
            if (visual != null)
            {
                AddVisualChildInternal(visual, item.ZIndex);
                _visualMap.Add(item, visual);
                _itemMap.Add(visual, item);
                _itemsAdded++;
                InvalidateMeasure();
                InvalidateArrange();
            }
        }
        else
        {
            if (UpdateVisualChildZIndex(visual, item.ZIndex))
                _itemsChanged++;
        }

        return visual;
    }

    private void VirtualizeItem(ISpatialItem item)
    {
        if (_visualMap.TryGetValue(item, out var visual) && ShouldVirtualize(item, visual))
            ForceVirtualizeItem(item, visual);
    }

    /// <summary>Removes the visual for <paramref name="item"/> regardless of focus state.</summary>
    public void ForceVirtualizeItem(ISpatialItem item)
    {
        if (_visualMap.TryGetValue(item, out var visual))
            ForceVirtualizeItem(item, visual);
    }

    private void ForceVirtualizeItem(ISpatialItem item, Control visual)
    {
        RemoveVisualChildInternal(visual);
        _visualMap.Remove(item);
        _itemMap.Remove(visual);
        _itemsRemoved++;
    }

    /// <summary>Removes all realized visuals from the canvas.</summary>
    public void Clear()
    {
        foreach (var pair in _visualMap.ToList())
        {
            RemoveVisualChildInternal(pair.Value);
            _itemMap.Remove(pair.Value);
            _itemsRemoved++;
        }
        _visualMap.Clear();
        _sortedVisuals.Clear();
        // VisualChildren/LogicalChildren already cleared via RemoveVisualChildInternal above
    }

    private bool ShouldVirtualize(ISpatialItem item, Control visual)
    {
        if (Items == null || !item.IsVisible) return true;  // teardown / explicit hide always wins
        if (_pinnedItems.Contains(item)) return false;       // pin: consumer-managed, ref-based
        if (visual.IsFocused) return false;                  // focus: Avalonia keyboard focus
        return _factory.Virtualize(visual);
    }

    #region Pinning

    /// <summary>
    /// Prevents <paramref name="item"/> from being virtualized by normal realization passes.
    /// A pinned item's <see cref="Control"/> is kept alive even when the item is outside
    /// the viewbox or absent from the current snapshot.
    /// <para>
    /// <b>Contract:</b>
    /// <list type="bullet">
    ///   <item>Pin identity is reference-based (same as <c>_visualMap</c>).
    ///         Replacing an item with a new object instance does NOT transfer the pin.</item>
    ///   <item>Teardown (<c>Items == null</c>) and explicit hide
    ///         (<c>item.IsVisible == false</c>) override pinning.</item>
    ///   <item><see cref="ForceVirtualizeItem(ISpatialItem)"/> bypasses pinning by design.</item>
    ///   <item>The consumer is responsible for calling <see cref="Unpin"/> when the item
    ///         no longer needs protection (e.g., drag ends, selection cleared).</item>
    /// </list>
    /// </para>
    /// </summary>
    public void Pin(ISpatialItem item)   => _pinnedItems.Add(item);

    /// <summary>Removes the pin for <paramref name="item"/>. No-op if not pinned.</summary>
    public void Unpin(ISpatialItem item) => _pinnedItems.Remove(item);

    /// <summary>Returns <c>true</c> if <paramref name="item"/> is currently pinned.</summary>
    public bool IsPinned(ISpatialItem item) => _pinnedItems.Contains(item);

    #endregion

    /// <summary>Returns the realized control for <paramref name="item"/>, or <c>null</c>.</summary>
    public Control? VisualFromItem(ISpatialItem item)
    {
        _visualMap.TryGetValue(item, out var v);
        return v;
    }

    /// <summary>Returns the spatial item that owns <paramref name="visual"/>, or <c>null</c>.</summary>
    public ISpatialItem? ItemFromVisual(Control visual)
    {
        _itemMap.TryGetValue(visual, out var item);
        return item;
    }

    #endregion

    #region RealizeOverride

    private IEnumerator RealizeOverride()
    {
        _factory.BeginRealize();
        var realizedItems = new HashSet<ISpatialItem>();

        if (Items != null)
        {
            IEnumerable<ISpatialItem> itemsToRealize = IsVirtualizing
                ? Items.GetItemsIntersecting(ActualViewbox).ToList()
                : (IEnumerable<ISpatialItem>)Items;

            var itemEnum = itemsToRealize.GetEnumerator();
            QuantizedWorkHandler realizeHandler = q => RealizeItemsBatch(itemEnum, realizedItems, q);

            while (SelfThrottlingWorker(ref _realizationQuantum, realizeHandler))
                yield return null;

            if (realizedItems.Count > 0)
                VisualChildrenChanged?.Invoke(this, EventArgs.Empty);
        }

        var toVirtualize = new List<ISpatialItem>(_visualMap.Count);
        foreach (var kvp in _visualMap)
        {
            if (!realizedItems.Contains(kvp.Key) && ShouldVirtualize(kvp.Key, kvp.Value))
                toVirtualize.Add(kvp.Key);
        }

        var virtEnum = toVirtualize.GetEnumerator();
        QuantizedWorkHandler virtualizeHandler = q => VirtualizeItemsBatch(virtEnum, q);

        while (SelfThrottlingWorker(ref _virtualizationQuantum, virtualizeHandler))
            yield return null;

        _factory.EndRealize();
    }

    private int RealizeItemsBatch(
        IEnumerator<ISpatialItem> items, HashSet<ISpatialItem> realized, int max)
    {
        int count = 0;
        _factory.BeginRealize();
        while (count < max && items.MoveNext())
        {
            var item = items.Current;
            var visual = RealizeItem(item, false);
            if (visual != null)
            {
                count++;
                realized.Add(item);
            }
        }
        _factory.EndRealize();
        return count;
    }

    private int VirtualizeItemsBatch(IEnumerator<ISpatialItem> items, int max)
    {
        int count = 0;
        while (count < max && items.MoveNext())
        {
            VirtualizeItem(items.Current);
            count++;
        }
        return count;
    }

    #endregion

    #region Measure / Arrange

    protected override Size MeasureOverride(Size availableSize)
    {
        _doingLayout = true;
        try
        {
            Measuring?.Invoke(this, EventArgs.Empty);

            foreach (var (visual, _) in _sortedVisuals)
                visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Measured?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _doingLayout = false;
        }

        return new Size();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        using (BeginUpdate())
        {
            _doingLayout = true;
            try
            {
                Measuring?.Invoke(this, EventArgs.Empty);

                foreach (var (visual, _) in _sortedVisuals)
                {
                    if (!_itemMap.TryGetValue(visual, out var item)) continue;

                    var b = item.Bounds;
                    double x = Math.Max(b.X, (double)(float.MinValue / 2));
                    double y = Math.Max(b.Y, (double)(float.MinValue / 2));
                    double w = Math.Min(visual.DesiredSize.Width, float.MaxValue);
                    double h = Math.Min(visual.DesiredSize.Height, float.MaxValue);
                    visual.Arrange(new Rect(x, y, w, h));
                }

                Measured?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _doingLayout = false;
            }

            UpdateActualViewboxWithSize(finalSize);
            return finalSize;
        }
    }

    #endregion

    #region Helpers

    private sealed class DisposableAction : IDisposable
    {
        private Action? _action;
        public DisposableAction(Action action) => _action = action;
        public void Dispose() { _action?.Invoke(); _action = null; }
    }

    #endregion
}
