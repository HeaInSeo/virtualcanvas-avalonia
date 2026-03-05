namespace VirtualCanvas.Core.Geometry;

/// <summary>
/// Minimal axis-aligned rectangle for VirtualCanvas.Core spatial computations.
/// Semantics mirror WPF System.Windows.Rect where it matters for the QuadTree:
///   - Empty sentinel: (X=+Inf, Y=+Inf, Width=-Inf, Height=-Inf)
///   - IntersectsWith returns true if either rect is empty (mirrors WpfHelper.Intersects)
///   - Boundary-touching counts as intersection
///   - PositiveInfinity Width/Height handled for the Infinite sentinel
/// </summary>
public readonly struct VCRect : IEquatable<VCRect>
{
    // WPF-style Empty: special sentinel, not a real area.
    // Width < 0 → IsEmpty. Only VCRect.Empty should have Width < 0.
    private static readonly VCRect s_empty = new VCRect(
        double.PositiveInfinity, double.PositiveInfinity,
        double.NegativeInfinity, double.NegativeInfinity);

    private readonly double _x;
    private readonly double _y;
    private readonly double _width;
    private readonly double _height;

    public VCRect(double x, double y, double width, double height)
    {
        _x = x;
        _y = y;
        _width = width;
        _height = height;
    }

    /// <summary>
    /// Special empty sentinel. IsEmpty = true, IsDefined = false.
    /// </summary>
    public static VCRect Empty => s_empty;

    /// <summary>
    /// Covers the entire infinite plane. Used as "query everything" sentinel in QuadTree.
    /// IsDefined = true, IsEmpty = false.
    /// </summary>
    public static VCRect Infinite { get; } = new VCRect(
        double.NegativeInfinity, double.NegativeInfinity,
        double.PositiveInfinity, double.PositiveInfinity);

    public double X => _x;
    public double Y => _y;
    public double Width => _width;
    public double Height => _height;

    public double Left => _x;
    public double Top => _y;
    public double Right => _x + _width;
    public double Bottom => _y + _height;

    /// <summary>
    /// True if this is the Empty sentinel (Width &lt; 0).
    /// A zero-size rect (0,0,0,0) is NOT empty.
    /// </summary>
    public bool IsEmpty => _width < 0;

    /// <summary>
    /// True if this rect is well-defined: not Empty, no NaN fields, no forbidden infinities.
    /// Mirrors WpfHelper.IsDefined. Infinite passes; Empty fails.
    /// </summary>
    public bool IsDefined =>
        _y < double.PositiveInfinity &&
        _x < double.PositiveInfinity &&
        _width > double.NegativeInfinity &&
        _height > double.NegativeInfinity;

    /// <summary>
    /// True if this rect fully contains <paramref name="rect"/>.
    /// Returns false if either is Empty.
    /// </summary>
    public bool Contains(VCRect rect)
    {
        if (IsEmpty || rect.IsEmpty)
            return false;
        return Left <= rect.Left && Top <= rect.Top &&
               Right >= rect.Right && Bottom >= rect.Bottom;
    }

    /// <summary>
    /// True if this rect intersects <paramref name="rect"/>.
    /// Mirrors WpfHelper.Intersects — NOT Rect.IntersectsWith:
    ///   • Returns true if either is Empty (defensive, preserves WPF ref semantics)
    ///   • Boundary-touching (shared edge) counts as intersection (uses >=)
    ///   • PositiveInfinity Width/Height handled for the Infinite sentinel
    /// </summary>
    public bool IntersectsWith(VCRect rect)
    {
        if (IsEmpty || rect.IsEmpty)
            return true; // preserve WpfHelper.Intersects semantics

        return (_width == double.PositiveInfinity || Right >= rect.Left) &&
               (rect._width == double.PositiveInfinity || rect.Right >= Left) &&
               (_height == double.PositiveInfinity || Bottom >= rect.Top) &&
               (rect._height == double.PositiveInfinity || rect.Bottom >= Top);
    }

    public bool Equals(VCRect other) =>
        _x == other._x && _y == other._y &&
        _width == other._width && _height == other._height;

    public override bool Equals(object? obj) => obj is VCRect r && Equals(r);

    public override int GetHashCode() => HashCode.Combine(_x, _y, _width, _height);

    public static bool operator ==(VCRect a, VCRect b) => a.Equals(b);
    public static bool operator !=(VCRect a, VCRect b) => !a.Equals(b);

    public override string ToString() =>
        IsEmpty ? "VCRect.Empty" : $"VCRect({_x}, {_y}, {_width}, {_height})";
}
