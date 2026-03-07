using Avalonia;
using Avalonia.Headless.XUnit;
using Xunit;

// Alias avoids conflict between root namespace 'VirtualCanvas' and the class.
using VCCanvas = VirtualCanvas.Avalonia.Controls.VirtualCanvas;

namespace VirtualCanvas.Avalonia.Tests;

/// <summary>
/// Contract tests for the VCA viewport coordinate system.
///
/// Documented contract (README §Coordinate System):
///   screen = world × scale − offset
///   world  = (screen + offset) / scale
///
/// These tests lock the formula so that future refactors and DagEdit
/// integration cannot silently break the coordinate seam.
///
/// DagEdit mapping:
///   VCA.Scale  ↔  DagEdit.ViewportScale
///   VCA.Offset ↔  DagEdit.ViewportLocation
/// </summary>
public class ViewportContractTests
{
    // ─────────────────────────────────────────────────────────────────
    // Pure-math [Fact] tests — no Avalonia dependency.
    // Document the formula independently of any control implementation.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>screen = world × scale − offset</summary>
    [Fact]
    public void ViewportMath_WorldToScreen_Formula()
    {
        double scale = 2.0, offsetX = 100.0, worldX = 300.0;
        double screenX = worldX * scale - offsetX;
        Assert.Equal(500.0, screenX);
    }

    /// <summary>world = (screen + offset) / scale</summary>
    [Fact]
    public void ViewportMath_ScreenToWorld_Formula()
    {
        double scale = 2.0, offsetX = 100.0, screenX = 500.0;
        double worldX = (screenX + offsetX) / scale;
        Assert.Equal(300.0, worldX);
    }

    /// <summary>screen → world → screen must recover the original screen coordinate.</summary>
    [Fact]
    public void ViewportMath_RoundTrip_ScreenWorldScreen()
    {
        double scale = 3.7, offsetX = 250.0, offsetY = 125.0;
        double screenX = 640.0, screenY = 360.0;

        double worldX = (screenX + offsetX) / scale;
        double worldY = (screenY + offsetY) / scale;

        double backX = worldX * scale - offsetX;
        double backY = worldY * scale - offsetY;

        Assert.Equal(screenX, backX, 10);
        Assert.Equal(screenY, backY, 10);
    }

    /// <summary>
    /// Cursor-centred zoom formula:
    ///   newOffset = worldUnderCursor × newScale − cursorScreen
    /// After applying newScale and newOffset, the world point that was
    /// under the cursor remains under the cursor.
    /// </summary>
    [Fact]
    public void ViewportMath_ZoomPivot_WorldPointUnderCursorPreserved()
    {
        double oldScale = 1.0, newScale = 2.0;
        double oldOffsetX = 0.0;
        double cursorX = 400.0;

        double worldX = (cursorX + oldOffsetX) / oldScale;     // 400
        double newOffsetX = worldX * newScale - cursorX;        // 400
        double worldXAfter = (cursorX + newOffsetX) / newScale; // 400

        Assert.Equal(worldX, worldXAfter, 10);
    }

    // ─────────────────────────────────────────────────────────────────
    // ActualViewbox property tests — require Avalonia UI thread.
    //
    // Tested invariant (origin only; Width/Height require a layout pass):
    //   ActualViewbox.X = Offset.X / Scale
    //   ActualViewbox.Y = Offset.Y / Scale
    //
    // This is the minimum seam VCA must honour for DagEdit integration:
    // GetItemsIntersecting(ActualViewbox) returns items visible in the
    // current viewport given the consumer-supplied Scale and Offset.
    // ─────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ActualViewbox_DefaultState_OriginIsZero()
    {
        // Scale=1, Offset=(0,0) → origin=(0/1, 0/1) = (0,0)
        var canvas = new VCCanvas();
        Assert.Equal(0.0, canvas.ActualViewbox.X, 10);
        Assert.Equal(0.0, canvas.ActualViewbox.Y, 10);
    }

    [AvaloniaFact]
    public void ActualViewbox_OriginFormula_NonTrivialOffsetAndScale()
    {
        // Offset=(100,50), Scale=2 → origin=(50,25)
        var canvas = new VCCanvas { Scale = 2.0, Offset = new Point(100.0, 50.0) };
        Assert.Equal(50.0, canvas.ActualViewbox.X, 10);
        Assert.Equal(25.0, canvas.ActualViewbox.Y, 10);
    }

    [AvaloniaFact]
    public void ActualViewbox_OriginFormula_UpdatesOnScaleChange()
    {
        var canvas = new VCCanvas { Offset = new Point(300.0, 150.0) };
        canvas.Scale = 3.0;
        Assert.Equal(100.0, canvas.ActualViewbox.X, 10); // 300 / 3
        Assert.Equal(50.0, canvas.ActualViewbox.Y, 10);  // 150 / 3
    }

    [AvaloniaFact]
    public void ActualViewbox_OriginFormula_UpdatesOnOffsetChange()
    {
        var canvas = new VCCanvas { Scale = 4.0 };
        canvas.Offset = new Point(400.0, 200.0);
        Assert.Equal(100.0, canvas.ActualViewbox.X, 10); // 400 / 4
        Assert.Equal(50.0, canvas.ActualViewbox.Y, 10);  // 200 / 4
    }
}
