using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VirtualCanvas.Avalonia.Tests.Helpers;
using VirtualCanvas.Core.Geometry;
using Xunit;

// Alias: avoids conflict between the root namespace 'VirtualCanvas' and the class.
using VCCanvas = VirtualCanvas.Avalonia.Controls.VirtualCanvas;

namespace VirtualCanvas.Avalonia.Tests;

/// <summary>
/// A-5.1 regression tests for the two confirmed bugs:
///   Bug 1 — RealizationCompleted event never fired on normal completion.
///   Bug 2 — LogicalChildren wrong element removed after ZIndex reorder.
/// </summary>
public class VirtualCanvasTests
{
    // ─────────────────────────────────────────────────────────────────
    // Bug 1: RealizationCompleted must fire after normal realization
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression: RealizeCore() set _realizeCts = null before continueAction's
    /// ReferenceEquals guard, causing OnRealizationComplete() to never be called.
    /// Fix: removed _realizeCts = null from RealizeCore(); ownership stays in continueAction.
    /// </summary>
    [AvaloniaFact]
    public async Task RealizationCompleted_Fires_AfterNormalRealization()
    {
        var index = new TestSpatialIndex();
        index.AddItem(new TestSpatialItem { Bounds = new VCRect(0, 0, 100, 100) });

        var canvas = new VCCanvas
        {
            VisualFactory = new ControlFactory(),
            IsVirtualizing = false   // avoid ActualViewbox=zero filtering items out
        };

        bool fired = false;
        canvas.RealizationCompleted += (_, _) => fired = true;

        // Setting Items triggers InvalidateReality() → Post(continueAction, Input)
        canvas.Items = index;

        // Yield below Input priority so all Input-level realization batches complete first.
        // Avalonia priority: Input(5) > Background(4), so all Posted continueActions
        // run before this InvokeAsync continuation resumes.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.True(fired, "RealizationCompleted should fire after normal realization completes.");
    }

    /// <summary>
    /// NotifyOnRealizationCompleted callback must be invoked after realization finishes.
    /// </summary>
    [AvaloniaFact]
    public async Task NotifyOnRealizationCompleted_CallbackInvoked_AfterRealization()
    {
        var index = new TestSpatialIndex();
        index.AddItem(new TestSpatialItem { Bounds = new VCRect(0, 0, 50, 50) });

        var canvas = new VCCanvas
        {
            VisualFactory = new ControlFactory(),
            IsVirtualizing = false
        };

        canvas.Items = index;

        // Wait for all Input-priority dispatcher work to complete.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        // Realization is done → _realizeCts == null → callback fires synchronously.
        bool callbackInvoked = false;
        canvas.NotifyOnRealizationCompleted(() => callbackInvoked = true);

        Assert.True(callbackInvoked, "NotifyOnRealizationCompleted callback should be called.");
    }

    /// <summary>
    /// Multi-batch boundary regression for Bug 1.
    /// 11 items + ThrottlingLimit=10 forces exactly two dispatch batches (10 + 1).
    /// RealizationCompleted must fire exactly once after the second (final) batch,
    /// not zero times (as with the bug) and not twice.
    /// </summary>
    [AvaloniaFact]
    public async Task RealizationCompleted_Fires_AfterNormalRealization_MultiBatch()
    {
        var index = new TestSpatialIndex();
        for (int i = 0; i < 11; i++)
            index.AddItem(new TestSpatialItem { Bounds = new VCRect(i * 10, 0, 10, 10) });

        var canvas = new VCCanvas
        {
            VisualFactory = new ControlFactory(),
            IsVirtualizing = false,   // avoid ActualViewbox=zero filtering items out
            ThrottlingLimit = 10      // batch 1: 10 items (workDone==work → yield)
                                      // batch 2:  1 item  (workDone <  work → done)
        };

        int firedCount = 0;
        canvas.RealizationCompleted += (_, _) => firedCount++;

        canvas.Items = index;

        // Input(5) > Background(4): both Input-level batches complete before this resumes.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.Equal(1, firedCount);
    }

    // ─────────────────────────────────────────────────────────────────
    // Bug 2: LogicalChildren integrity after ZIndex reorder + remove
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression: RemoveVisualChildInternal used index from _sortedVisuals to call
    /// LogicalChildren.RemoveAt(), but after UpdateVisualChildZIndex the indices diverged,
    /// causing the WRONG control to be removed from LogicalChildren.
    /// Fix: LogicalChildren removal is now reference-based (LogicalChildren.Remove(visual)).
    /// </summary>
    [AvaloniaFact]
    public void LogicalChildren_Integrity_AfterZIndexReorderAndRemove()
    {
        var itemA = new TestSpatialItem { Bounds = new VCRect(0, 0, 10, 10), ZIndex = 0 };
        var itemB = new TestSpatialItem { Bounds = new VCRect(10, 0, 10, 10), ZIndex = 1 };
        var itemC = new TestSpatialItem { Bounds = new VCRect(20, 0, 10, 10), ZIndex = 2 };

        var canvas = new VCCanvas { VisualFactory = new ControlFactory() };

        // Realize all three synchronously via the public API.
        canvas.RealizeItem(itemA);
        canvas.RealizeItem(itemB);
        canvas.RealizeItem(itemC);

        var controlB = canvas.VisualFromItem(itemB)!;
        var controlC = canvas.VisualFromItem(itemC)!;

        // Trigger ZIndex reorder: A moves from position 0 to the end.
        // RealizeItem on an already-realized item with a changed ZIndex
        // calls UpdateVisualChildZIndex internally.
        itemA.ZIndex = 3;
        canvas.RealizeItem(itemA);
        // State after reorder:
        //   _sortedVisuals = [(B,1), (C,2), (A,3)]
        //   VisualChildren = [B, C, A]
        //   LogicalChildren was [A, B, C] — desync, but removal is reference-based now

        // Remove A.
        canvas.ForceVirtualizeItem(itemA);

        // A must be gone from the canvas.
        Assert.Null(canvas.VisualFromItem(itemA));

        // B and C must still be present in the visual map.
        Assert.Equal(controlB, canvas.VisualFromItem(itemB));
        Assert.Equal(controlC, canvas.VisualFromItem(itemC));

        // B and C must retain canvas as their logical parent.
        // Bug scenario: LogicalChildren.RemoveAt(2) on [A,B,C] removes C, not A.
        // With the fix: LogicalChildren.Remove(controlA) removes the right element.
        Assert.Equal(canvas, controlB.Parent);
        Assert.Equal(canvas, controlC.Parent);
    }
}
