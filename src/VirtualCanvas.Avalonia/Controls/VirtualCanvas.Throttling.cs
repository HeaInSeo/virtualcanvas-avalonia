using System.Collections;
using System.Diagnostics;
using Avalonia.Threading;

namespace VirtualCanvas.Avalonia.Controls;

/// <summary>
/// Throttling and async realization scheduling for <see cref="VirtualCanvas"/>.
/// Avalonia 11 port of the WPF VirtualCanvas.Throttling.cs partial class.
/// </summary>
public partial class VirtualCanvas
{
    #region Fields

    private int _itemsAdded;
    private int _itemsRemoved;
    private int _itemsChanged;

    private int _minimumQuantum = 10;
    private int _throttlingLimit = 0;
    private long _idealDuration = 10; // milliseconds per batch
    private int _realizationQuantum = 10;
    private int _virtualizationQuantum = 50;

    private readonly Stopwatch _throttlingWatch = new();

    private delegate int QuantizedWorkHandler(int quantum);

    #endregion

    #region Pause / BeginUpdate / EndUpdate

    private int _paused;
    private bool _realizeCoreWithThrottlingPending;

    public bool IsPaused => _paused > 0;

    public event EventHandler? BeginChanges;
    public event EventHandler? EndChanges;

    /// <summary>
    /// Suspends realization. Returns a disposable that calls <see cref="EndUpdate"/> on dispose.
    /// </summary>
    public IDisposable BeginUpdate()
    {
        CancelRealization();
        if (_paused++ == 0)
            BeginChanges?.Invoke(this, EventArgs.Empty);
        return new DisposableAction(EndUpdate);
    }

    /// <summary>Resumes realization and fires any pending batches.</summary>
    public void EndUpdate()
    {
        if (_paused <= 0) return;
        _paused--;
        if (_paused == 0)
        {
            if (_realizeCoreWithThrottlingPending)
                RealizeCoreWithThrottling();
            EndChanges?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion

    #region InvalidateReality

    /// <summary>
    /// Invalidates the current realization state and schedules an async re-realization pass.
    /// </summary>
    public void InvalidateReality()
    {
        CancelRealization();
        _realizeCoreWithThrottlingPending = true;
        if (_paused == 0)
            RealizeCoreWithThrottling();
    }

    #endregion

    #region ThrottlingLimit

    /// <summary>
    /// Fixed number of items to realize/virtualize per dispatch cycle.
    /// Set to &lt;= 0 for auto-throttling (default).
    /// </summary>
    public int ThrottlingLimit
    {
        get => _throttlingLimit;
        set => _throttlingLimit = value;
    }

    #endregion

    #region Dispatcher Scheduling

    private CancellationTokenSource? _realizeCts;

    private void RealizeCoreWithThrottling()
    {
        _realizeCoreWithThrottlingPending = false;

        // Cancel any in-flight realization.
        _realizeCts?.Cancel();
        _realizeCts?.Dispose();
        var cts = _realizeCts = new CancellationTokenSource();

        IEnumerator? enumerator = null;
        Action? continueAction = null;
        continueAction = () =>
        {
            if (cts.IsCancellationRequested) return;

            enumerator = RealizeCore(enumerator);

            if (enumerator != null && !cts.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(continueAction!, DispatcherPriority.Input);
            }
            else if (ReferenceEquals(_realizeCts, cts))
            {
                _realizeCts = null;
                cts.Dispose();
                OnRealizationComplete();
            }
        };

        Dispatcher.UIThread.Post(continueAction, DispatcherPriority.Input);
    }

    private void CancelRealization()
    {
        if (_realizeCts != null)
        {
            _realizeCts.Cancel();
            _realizeCts.Dispose();
            _realizeCts = null;
            _realizeCoreWithThrottlingPending = true;
        }
    }

    /// <summary>
    /// Performs one chunk of <see cref="RealizeOverride"/>.
    /// Returns non-null if more work remains.
    /// </summary>
    private IEnumerator? RealizeCore(IEnumerator? enumerator)
    {
        var realizationEnumerator = enumerator ?? RealizeOverride();
        if (realizationEnumerator.MoveNext())
            return realizationEnumerator;

        _realizeCts = null;
        return null;
    }

    #endregion

    #region Self-Throttling Worker

    /// <summary>
    /// Calls <paramref name="handler"/> for one quantum of work,
    /// adjusting <paramref name="quantum"/> to hit <see cref="_idealDuration"/> ms per call.
    /// Returns <c>true</c> if there is more work remaining.
    /// </summary>
    private bool SelfThrottlingWorker(ref int quantum, QuantizedWorkHandler handler)
    {
        int work;
        int workDone;

        if (_throttlingLimit > 0)
        {
            work = _throttlingLimit;
            workDone = handler(work);
        }
        else
        {
            work = quantum;
            _throttlingWatch.Restart();
            workDone = handler(work);
            _throttlingWatch.Stop();
            long duration = _throttlingWatch.ElapsedMilliseconds;
            if (workDone > 0 && duration > 0)
            {
                long adjusted = (workDone * _idealDuration) / duration;
                quantum = Math.Max(_minimumQuantum, (int)Math.Min(adjusted, int.MaxValue));
            }
        }

        return workDone >= work; // true → more work may remain
    }

    #endregion

    #region Realization Complete

    public event EventHandler? RealizationCompleted;

    private readonly Queue<Action> _realizationDelegates = new();

    /// <summary>
    /// Enqueues <paramref name="action"/> to be called once the current realization pass finishes.
    /// If realization is already complete, <paramref name="action"/> is called immediately.
    /// </summary>
    public void NotifyOnRealizationCompleted(Action action)
    {
        _realizationDelegates.Enqueue(action);
        if (_realizeCts == null)
            OnRealizationComplete();
    }

    private void OnRealizationComplete()
    {
        while (_realizationDelegates.Count > 0)
            _realizationDelegates.Dequeue()();

        if (_itemsAdded > 0 || _itemsRemoved > 0 || _itemsChanged > 0)
        {
            _itemsAdded = _itemsRemoved = _itemsChanged = 0;
            RealizationCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion
}
