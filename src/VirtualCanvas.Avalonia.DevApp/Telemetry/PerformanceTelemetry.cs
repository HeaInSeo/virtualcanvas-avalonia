namespace VirtualCanvas.Avalonia.DevApp.Telemetry;

/// <summary>
/// Lightweight UI-thread-only performance telemetry.
/// <para>
/// All public methods MUST be called from the UI thread.
/// Uses EMA for frame time and 1-second integer buckets for event rates
/// to keep allocations at zero during measurement.
/// </para>
/// </summary>
internal sealed class PerformanceTelemetry
{
    // ── Frame time — EMA over ~7 samples (α = 0.15) ──────────────────
    // Seeded with -1 to distinguish "no sample yet".
    private TimeSpan _lastFrameTs = TimeSpan.FromMilliseconds(-1);
    private double _frameEmaMs;

    /// <summary>Call from RequestAnimationFrame callback each frame.</summary>
    public void RecordFrame(TimeSpan ts)
    {
        if (_lastFrameTs.TotalMilliseconds >= 0)
        {
            double dt = (ts - _lastFrameTs).TotalMilliseconds;
            // Clamp to plausible range; first sample seeds the EMA.
            if (dt is > 0.5 and < 500)
                _frameEmaMs = _frameEmaMs == 0 ? dt : _frameEmaMs * 0.85 + dt * 0.15;
        }
        _lastFrameTs = ts;
    }

    /// <summary>Exponential moving average of inter-frame interval (ms).</summary>
    public double FrameTimeMs => _frameEmaMs;

    // ── 1-second bucket counters ──────────────────────────────────────
    // On each Record* call we check if the current second has changed.
    // If so, the previous bucket is promoted to the public Rate property
    // and a new bucket starts. This means "Rate" lags by up to 1 second
    // but requires zero allocations.
    private long _bucketSec = -1;
    private int _panBucket, _zoomBucket, _realizeBucket;

    public int PanRate { get; private set; }
    public int ZoomRate { get; private set; }

    /// <summary>RealizationCompleted events per second (fires only when items actually changed).</summary>
    public int RealizeRate { get; private set; }

    private void FlushBuckets()
    {
        long sec = Environment.TickCount64 / 1000;
        if (sec == _bucketSec) return;
        PanRate = _panBucket;         _panBucket = 0;
        ZoomRate = _zoomBucket;       _zoomBucket = 0;
        RealizeRate = _realizeBucket; _realizeBucket = 0;
        _bucketSec = sec;
    }

    public void RecordPan()         { FlushBuckets(); _panBucket++; }
    public void RecordZoom()        { FlushBuckets(); _zoomBucket++; }
    public void RecordRealization() { FlushBuckets(); _realizeBucket++; }

    // ── GC allocation delta (UI thread, 1-second snapshot) ───────────
    // GC.GetAllocatedBytesForCurrentThread() is UI-thread only.
    // The 1-second delta gives approximate KB/s of allocation pressure
    // induced by UI work (render, realization, string formatting, etc.).
    private long _lastAllocBytes = -1;

    /// <summary>UI-thread allocation rate in KB/s (approximate; 1-second delta).</summary>
    public double AllocKBps { get; private set; }

    // ── Realized count delta ─────────────────────────────────────────
    private int _lastRealized;

    /// <summary>
    /// Change in realized item count since the previous <see cref="Snapshot"/> call.
    /// Positive = items added, negative = virtualized away.
    /// </summary>
    public int RealizedDelta { get; private set; }

    // ── Snapshot ─────────────────────────────────────────────────────

    /// <summary>
    /// Call once per second from a DispatcherTimer.
    /// Captures the GC allocation delta and realized-count delta.
    /// Also flushes the current event bucket so Rate values are up to date.
    /// </summary>
    public void Snapshot(int realizedCount)
    {
        FlushBuckets();

        long alloc = GC.GetAllocatedBytesForCurrentThread();
        if (_lastAllocBytes >= 0)
            AllocKBps = (alloc - _lastAllocBytes) / 1024.0;
        _lastAllocBytes = alloc;

        RealizedDelta = realizedCount - _lastRealized;
        _lastRealized = realizedCount;
    }
}
