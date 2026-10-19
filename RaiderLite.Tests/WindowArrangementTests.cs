using System.Drawing;
using RaiderLite.Core.Layout;

namespace RaiderLite.Tests;

public class WindowArrangementTests
{
    private static readonly Size Area = new(1000, 700);
    private static readonly Size Min = new(240, 180);

    private static void AssertInsideAndApart(IReadOnlyList<Rectangle> rects, Size area)
    {
        foreach (var r in rects)
            Assert.True(new Rectangle(Point.Empty, area).Contains(r), $"{r} is outside {area}");
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
                Assert.False(rects[i].IntersectsWith(rects[j]), $"{rects[i]} overlaps {rects[j]}");
    }

    [Fact]
    public void ArrangeKeepsPreferredSizesWhenTheyFit()
    {
        Size[] sizes = [new(500, 400), new(300, 200), new(400, 250)];
        var rects = WindowArrangement.Arrange(sizes, Area, Min);
        Assert.Equal(sizes, rects.Select(r => r.Size));
        Assert.Equal(new Point(0, 0), rects[0].Location);
        Assert.Equal(new Point(500, 0), rects[1].Location);
        Assert.Equal(new Point(0, 400), rects[2].Location);   // wraps to the next row
        AssertInsideAndApart(rects, Area);
    }

    [Fact]
    public void ArrangeSharesTheRoomWhenTheyDontFit()
    {
        Size[] sizes = [new(900, 500), new(900, 500)];
        var rects = WindowArrangement.Arrange(sizes, Area, Min);
        AssertInsideAndApart(rects, Area);
        Assert.Equal(rects[0].Size, rects[1].Size);
        // Stacked shows 70% of each; shrinking both evenly into rows would show 49%.
        Assert.All(rects, r => Assert.True(r.Width * r.Height >= 0.69 * 900 * 500, r.ToString()));
    }

    [Fact]
    public void ArrangeGivesALargeTableItsRoomBesideSmallOnes()
    {
        // A big map and two small curves: the map on the left, the curves stacked beside it.
        Size[] sizes = [new(300, 200), new(650, 680), new(300, 200)];
        var rects = WindowArrangement.Arrange(sizes, Area, Min);
        AssertInsideAndApart(rects, Area);
        Assert.Equal(sizes, rects.Select(r => r.Size));
    }

    [Fact]
    public void ArrangeFallsBackToAGridForManyWindows()
    {
        var sizes = Enumerable.Repeat(new Size(900, 650), 7).ToList();
        var rects = WindowArrangement.Arrange(sizes, Area, Min);
        Assert.Equal(7, rects.Count);
        AssertInsideAndApart(rects, Area);
    }

    [Fact]
    public void SideBySideUsesPreferredWidthsWhenTheyFit()
    {
        Size[] sizes = [new(300, 900), new(450, 250)];
        var rects = WindowArrangement.SideBySide(sizes, Area, Min);
        Assert.Equal([new Rectangle(0, 0, 300, 700), new Rectangle(300, 0, 450, 250)], rects);
    }

    [Fact]
    public void SideBySideShrinksInProportion()
    {
        Size[] sizes = [new(800, 300), new(400, 300)];
        var rects = WindowArrangement.SideBySide(sizes, Area, Min);
        AssertInsideAndApart(rects, Area);
        Assert.True(rects[0].Width > rects[1].Width);
        Assert.Equal(1000, rects.Sum(r => r.Width), 2.0);
    }

    [Fact]
    public void StackedShrinksInProportion()
    {
        Size[] sizes = [new(600, 600), new(600, 300)];
        var rects = WindowArrangement.Stacked(sizes, Area, Min);
        AssertInsideAndApart(rects, Area);
        Assert.True(rects[0].Height > rects[1].Height);
        Assert.All(rects, r => Assert.Equal(600, r.Width));
    }

    [Fact]
    public void CascadeOffsetsWindowsAtTheirOwnSizes()
    {
        Size[] sizes = [new(500, 400), new(300, 200)];
        var rects = WindowArrangement.Cascade(sizes, Area, Min, 30);
        Assert.Equal([new Rectangle(0, 0, 500, 400), new Rectangle(30, 30, 300, 200)], rects);
    }

    [Fact]
    public void NothingToArrange()
    {
        Assert.Empty(WindowArrangement.Arrange([], Area, Min));
        Assert.Empty(WindowArrangement.SideBySide([], Area, Min));
    }
}
