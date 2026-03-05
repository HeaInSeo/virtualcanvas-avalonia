using Xunit;
using VirtualCanvas.Core.Geometry;
using VirtualCanvas.Core.Spatial;

namespace VirtualCanvas.Core.Tests.Spatial;

// ──────────────────────────────────────────────
// VCRect unit tests
// ──────────────────────────────────────────────

public class VCRectTests
{
    [Fact]
    public void Ctor_SetsFields()
    {
        var r = new VCRect(1, 2, 3, 4);
        Assert.Equal(1, r.X); Assert.Equal(2, r.Y);
        Assert.Equal(3, r.Width); Assert.Equal(4, r.Height);
        Assert.Equal(1, r.Left); Assert.Equal(2, r.Top);
        Assert.Equal(4, r.Right); Assert.Equal(6, r.Bottom);
    }

    [Fact]
    public void Empty_IsEmpty()
    {
        Assert.True(VCRect.Empty.IsEmpty);
        Assert.False(VCRect.Empty.IsDefined);
    }

    [Fact]
    public void ZeroSizeRect_IsNotEmpty()
    {
        var r = new VCRect(0, 0, 0, 0);
        Assert.False(r.IsEmpty);
        Assert.True(r.IsDefined);
    }

    [Fact]
    public void Infinite_IsDefinedAndNotEmpty()
    {
        Assert.False(VCRect.Infinite.IsEmpty);
        Assert.True(VCRect.Infinite.IsDefined);
    }

    [Fact]
    public void IntersectsWith_NormalOverlap_ReturnsTrue()
    {
        var a = new VCRect(0, 0, 10, 10);
        var b = new VCRect(5, 5, 10, 10);
        Assert.True(a.IntersectsWith(b));
        Assert.True(b.IntersectsWith(a));
    }

    [Fact]
    public void IntersectsWith_NoOverlap_ReturnsFalse()
    {
        var a = new VCRect(0, 0, 10, 10);
        var b = new VCRect(20, 20, 10, 10);
        Assert.False(a.IntersectsWith(b));
    }

    [Fact]
    public void IntersectsWith_BoundaryTouch_ReturnsTrue()
    {
        // Rects sharing only a single edge should intersect (>= semantics)
        var a = new VCRect(0, 0, 10, 10);
        var b = new VCRect(10, 0, 10, 10); // shares right/left edge
        Assert.True(a.IntersectsWith(b));
    }

    [Fact]
    public void IntersectsWith_EitherEmpty_ReturnsTrue()
    {
        // Preserves WpfHelper.Intersects semantics
        var a = new VCRect(0, 0, 10, 10);
        Assert.True(a.IntersectsWith(VCRect.Empty));
        Assert.True(VCRect.Empty.IntersectsWith(a));
    }

    [Fact]
    public void IntersectsWith_Infinite_ReturnsTrue()
    {
        var a = new VCRect(100, 200, 50, 50);
        Assert.True(a.IntersectsWith(VCRect.Infinite));
        Assert.True(VCRect.Infinite.IntersectsWith(a));
    }

    [Fact]
    public void Contains_FullContainment_ReturnsTrue()
    {
        var outer = new VCRect(0, 0, 100, 100);
        var inner = new VCRect(10, 10, 50, 50);
        Assert.True(outer.Contains(inner));
    }

    [Fact]
    public void Contains_PartialOverlap_ReturnsFalse()
    {
        var a = new VCRect(0, 0, 10, 10);
        var b = new VCRect(5, 5, 10, 10);
        Assert.False(a.Contains(b));
    }

    [Fact]
    public void Contains_EitherEmpty_ReturnsFalse()
    {
        var a = new VCRect(0, 0, 100, 100);
        Assert.False(a.Contains(VCRect.Empty));
        Assert.False(VCRect.Empty.Contains(a));
    }

    [Fact]
    public void Contains_ExactBoundary_ReturnsTrue()
    {
        var a = new VCRect(0, 0, 100, 100);
        Assert.True(a.Contains(a)); // rect contains itself
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new VCRect(1, 2, 3, 4);
        var b = new VCRect(1, 2, 3, 4);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var a = new VCRect(1, 2, 3, 4);
        var b = new VCRect(1, 2, 3, 5);
        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }
}

// ──────────────────────────────────────────────
// PriorityQuadTree unit tests
// ──────────────────────────────────────────────

public class PriorityQuadTreeTests
{
    private static PriorityQuadTree<string> MakeTree(double extent = 1000)
    {
        var tree = new PriorityQuadTree<string>();
        tree.Extent = new VCRect(0, 0, extent, extent);
        return tree;
    }

    // ── Empty tree ──────────────────────────────

    [Fact]
    public void EmptyTree_HasNoItems()
    {
        var tree = MakeTree();
        Assert.Empty(tree);
        Assert.False(tree.HasItemsIntersecting(new VCRect(0, 0, 100, 100)));
        Assert.False(tree.HasItemsInside(new VCRect(0, 0, 100, 100)));
    }

    // ── Insert + query (single item) ────────────

    [Fact]
    public void Insert_OneItem_FoundByIntersect()
    {
        var tree = MakeTree();
        tree.Insert("A", new VCRect(10, 10, 50, 50), 0);

        var query = new VCRect(0, 0, 100, 100);
        Assert.True(tree.HasItemsIntersecting(query));
        Assert.Contains("A", tree.GetItemsIntersecting(query));
    }

    [Fact]
    public void Insert_OneItem_NotFoundWhenQueryMisses()
    {
        var tree = MakeTree();
        tree.Insert("A", new VCRect(10, 10, 50, 50), 0);

        var query = new VCRect(200, 200, 50, 50);
        Assert.False(tree.HasItemsIntersecting(query));
        Assert.Empty(tree.GetItemsIntersecting(query));
    }

    [Fact]
    public void Insert_OneItem_FoundByInside()
    {
        var tree = MakeTree();
        tree.Insert("A", new VCRect(10, 10, 50, 50), 0);

        // GetItemsInside uses GetIntersectingNodes+filter — works for any query size.
        Assert.Contains("A", tree.GetItemsInside(new VCRect(0, 0, 100, 100)));

        // HasItemsInside uses Quadrant.HasNodesInside which only recurses into child
        // quadrants when the child contains the query. Works with InfiniteBounds.
        Assert.True(tree.HasItemsInside(VCRect.Infinite));
    }

    [Fact]
    public void Insert_OneItem_NotInsideWhenQuerySmaller()
    {
        var tree = MakeTree();
        tree.Insert("A", new VCRect(10, 10, 50, 50), 0);

        // Query rect is smaller than item — item is not "inside" query
        Assert.Empty(tree.GetItemsInside(new VCRect(20, 20, 10, 10)));
    }

    // ── Multiple items ──────────────────────────

    [Fact]
    public void Insert_MultipleItems_AllFoundByIntersect()
    {
        var tree = MakeTree();
        tree.Insert("A", new VCRect(0, 0, 10, 10), 0);
        tree.Insert("B", new VCRect(50, 50, 10, 10), 0);
        tree.Insert("C", new VCRect(200, 200, 10, 10), 0);

        var results = tree.GetItemsIntersecting(new VCRect(0, 0, 500, 500)).ToList();
        Assert.Equal(3, results.Count);
        Assert.Contains("A", results);
        Assert.Contains("B", results);
        Assert.Contains("C", results);
    }

    [Fact]
    public void Insert_MultipleItems_OnlyIntersectingReturned()
    {
        var tree = MakeTree();
        tree.Insert("A", new VCRect(0, 0, 10, 10), 0);
        tree.Insert("B", new VCRect(500, 500, 10, 10), 0);

        var results = tree.GetItemsIntersecting(new VCRect(0, 0, 20, 20)).ToList();
        Assert.Contains("A", results);
        Assert.DoesNotContain("B", results);
    }

    // ── Boundary touching ───────────────────────

    [Fact]
    public void Query_BoundaryTouch_ItemFound()
    {
        var tree = MakeTree();
        // Item sits exactly at the boundary of the query rect
        tree.Insert("A", new VCRect(100, 0, 10, 10), 0);

        // Query ends exactly at x=100 (Right = 100)
        var query = new VCRect(0, 0, 100, 100); // Right = 100
        // A.Left = 100 = query.Right → touching → IntersectsWith returns true
        var results = tree.GetItemsIntersecting(query).ToList();
        Assert.Contains("A", results);
    }

    // ── Priority ordering ───────────────────────

    [Fact]
    public void Priority_HigherNumericValueFirst()
    {
        var tree = MakeTree();
        tree.Insert("PriTen", new VCRect(10, 10, 10, 10), 10);
        tree.Insert("PriFive", new VCRect(20, 10, 10, 10), 5);
        tree.Insert("PriOne", new VCRect(30, 10, 10, 10), 1);

        var results = tree.GetItemsIntersecting(new VCRect(0, 0, 100, 100)).ToList();
        // WPF reference behavior: HIGHER numeric priority value is returned first.
        // QuadNode linked list: tail = smallest value; iteration starts from tail.Next = largest.
        // (The ISpatialIndex XML comment "lower values first" is misleading — actual behavior is opposite.)
        Assert.Equal("PriTen", results[0]);
        Assert.Equal("PriFive", results[1]);
        Assert.Equal("PriOne", results[2]);
    }

    // ── Remove ──────────────────────────────────

    [Fact]
    public void Remove_ExistingItem_RemovedFromResults()
    {
        var tree = MakeTree();
        tree.Insert("A", new VCRect(10, 10, 10, 10), 0);
        bool removed = tree.Remove("A");

        Assert.True(removed);
        Assert.Empty(tree.GetItemsIntersecting(VCRect.Infinite));
    }

    [Fact]
    public void Remove_NonExistingItem_ReturnsFalse()
    {
        var tree = MakeTree();
        tree.Insert("A", new VCRect(10, 10, 10, 10), 0);
        bool removed = tree.Remove("Z");
        Assert.False(removed);
    }

    [Fact]
    public void Remove_WithBounds_RemovedFromResults()
    {
        var tree = MakeTree();
        tree.Insert("A", new VCRect(10, 10, 10, 10), 0);
        bool removed = tree.Remove("A", new VCRect(0, 0, 50, 50));

        Assert.True(removed);
        Assert.Empty(tree.GetItemsIntersecting(VCRect.Infinite));
    }

    // ── Clear ───────────────────────────────────

    [Fact]
    public void Clear_RemovesAllItems()
    {
        var tree = MakeTree();
        tree.Insert("A", new VCRect(10, 10, 10, 10), 0);
        tree.Insert("B", new VCRect(50, 50, 10, 10), 0);
        tree.Clear();

        Assert.Empty(tree);
        Assert.False(tree.HasItemsIntersecting(VCRect.Infinite));
    }

    // ── emptyItems (0x0 bounds) ─────────────────

    [Fact]
    public void ZeroBoundsItem_StoredInEmptyItems_FoundByInfiniteQuery()
    {
        var tree = MakeTree();
        tree.Insert("Zero", new VCRect(0, 0, 0, 0), 0);

        var results = tree.GetItemsIntersecting(VCRect.Infinite).ToList();
        Assert.Contains("Zero", results);
    }

    [Fact]
    public void ZeroBoundsItem_FoundByContainingQuery()
    {
        var tree = MakeTree();
        tree.Insert("Zero", new VCRect(0, 0, 0, 0), 0);

        // GetItemsIntersecting(queryContainingOrigin) should find it
        var results = tree.GetItemsIntersecting(new VCRect(-1, -1, 2, 2)).ToList();
        Assert.Contains("Zero", results);
    }

    // ── Infinite query ──────────────────────────

    [Fact]
    public void InfiniteQuery_ReturnsAllItems()
    {
        var tree = MakeTree();
        tree.Insert("A", new VCRect(10, 10, 10, 10), 2);
        tree.Insert("B", new VCRect(500, 500, 10, 10), 1);
        tree.Insert("Zero", new VCRect(0, 0, 0, 0), 0);

        var results = tree.GetItemsIntersecting(VCRect.Infinite).ToList();
        Assert.Equal(3, results.Count);
    }

    // ── Extent / ReIndex ────────────────────────

    [Fact]
    public void ChangeExtent_ItemsStillFound()
    {
        var tree = MakeTree(100);
        tree.Insert("A", new VCRect(10, 10, 10, 10), 0);

        // Expand extent — triggers ReIndex
        tree.Extent = new VCRect(0, 0, 500, 500);

        var results = tree.GetItemsIntersecting(VCRect.Infinite).ToList();
        Assert.Contains("A", results);
    }

    [Fact]
    public void SetExtent_InvalidValue_Throws()
    {
        var tree = MakeTree();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tree.Extent = new VCRect(double.NaN, 0, 100, 100));
    }

    // ── Many items stability ─────────────────────

    [Fact]
    public void Insert_ManyItems_AllFound()
    {
        var tree = MakeTree(1000);
        var items = new List<string>();

        for (int i = 0; i < 200; i++)
        {
            string name = $"item_{i}";
            items.Add(name);
            tree.Insert(name, new VCRect(i * 4 % 900, i * 3 % 900, 8, 8), i);
        }

        var results = tree.GetItemsIntersecting(VCRect.Infinite).ToList();
        Assert.Equal(200, results.Count);

        foreach (var name in items)
            Assert.Contains(name, results);
    }

    // ── Insert invalid bounds ────────────────────

    [Fact]
    public void Insert_NaNBounds_Throws()
    {
        var tree = MakeTree();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tree.Insert("X", new VCRect(double.NaN, 0, 10, 10), 0));
    }
}
