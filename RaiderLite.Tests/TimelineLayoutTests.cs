using RaiderLite.Core.Tunes;

namespace RaiderLite.Tests;

public class TimelineLayoutTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0);

    /// <summary>A node created <paramref name="daysAgo"/> days before <see cref="Now"/>.</summary>
    private static TimelineNode N(int number, int? parent, int daysAgo = 30) =>
        new(number, parent, Now.AddDays(-daysAgo));

    private static IReadOnlyList<TimelineRow> Build(IReadOnlySet<int>? expanded = null, params TimelineNode[] nodes) =>
        TimelineLayout.Build(nodes, Now, expanded);

    private static TimelineRow Row(IReadOnlyList<TimelineRow> rows, int number) => rows.Single(r => r.Number == number && r.Fold == null);

    [Fact]
    public void NoRevisionsNoRows() => Assert.Empty(Build());

    [Fact]
    public void AStraightHistoryIsOneLane()
    {
        var rows = Build(null, N(0, null), N(1, 0), N(2, 1), N(3, 2));
        Assert.Equal([3, 2, 1, 0], rows.Select(r => r.Number));
        Assert.All(rows, r => Assert.Equal(0, r.Lane));
        Assert.Equal([false, true, true, true], rows.Select(r => r.LineUp));
        Assert.Equal([true, true, true, false], rows.Select(r => r.LineDown));
        Assert.All(rows, r => Assert.Empty(r.PassThrough));
        Assert.All(rows, r => Assert.Empty(r.BranchesIn));
    }

    [Fact]
    public void OnATieTheNewestChildStaysOnTheMainLine()
    {
        var rows = Build(null, N(0, null, 0), N(1, 0, 0), N(2, 1, 0), N(3, 1, 0));
        Assert.Equal(0, Row(rows, 3).Lane);
        var side = Row(rows, 2);
        Assert.Equal(1, side.Lane);
        Assert.False(side.LineUp);
        Assert.True(side.LineDown);
        Assert.Equal([0], side.PassThrough);       // the main line from 3 down to 1
        Assert.Equal([1], Row(rows, 1).BranchesIn);
    }

    [Fact]
    public void TheMainLineRunsToTheNewestRevisionNotTheLongestBranch()
    {
        // 1 forks into an old test chain 2-3-4 and the current line 5-6: the current line stays straight.
        var rows = Build(null, N(0, null, 0), N(1, 0, 0), N(2, 1, 0), N(3, 2, 0), N(4, 3, 0), N(5, 1, 0), N(6, 5, 0));
        Assert.Equal([0, 0, 0, 0], new[] { 0, 1, 5, 6 }.Select(n => Row(rows, n).Lane));
        Assert.All(new[] { 2, 3, 4 }, n => Assert.Equal(1, Row(rows, n).Lane));
    }

    [Fact]
    public void TheLongerBranchIsTheMainLine()
    {
        // 1 forks into 2 -> 4 (longer) and 3.
        var rows = Build(null, N(0, null, 0), N(1, 0, 0), N(2, 1, 0), N(3, 1, 0), N(4, 2, 0));
        Assert.Equal([0, 0, 0, 0], new[] { 0, 1, 2, 4 }.Select(n => Row(rows, n).Lane));
        Assert.Equal(1, Row(rows, 3).Lane);
        Assert.Equal([1], Row(rows, 2).PassThrough);   // 3's line passes 2 on its way to 1
        Assert.Equal([1], Row(rows, 1).BranchesIn);
        Assert.Empty(Row(rows, 4).PassThrough);
    }

    [Fact]
    public void FinishedSideBranchesOfOneParentFoldIntoOneRow()
    {
        // Like the 6 October tests: a rich test and four inj-angle variants off 25, then the main line goes on.
        var rows = Build(null,
            N(24, null), N(25, 24), N(26, 25), N(27, 25), N(28, 25), N(29, 25), N(30, 25), N(31, 25), N(32, 31));
        Assert.Equal([32, 31, 26, 25, 24], rows.Select(r => r.Number));
        var fold = rows.Single(r => r.Fold != null);
        Assert.Equal(26, fold.Number);
        Assert.Equal([26, 27, 28, 29, 30], fold.Fold!.Members);
        Assert.Equal(25, fold.Fold.Parent);
        Assert.Equal(1, fold.Lane);
        Assert.True(fold.LineDown);
        Assert.False(fold.LineUp);
        Assert.Equal([0], fold.PassThrough);
        Assert.Equal([1], Row(rows, 25).BranchesIn);
    }

    [Fact]
    public void FoldsIncludeBranchesOffTheFoldedBranch()
    {
        // 2 forks off 1 and itself forks into 3 and 4; the main line 1-5-6-7-8 is longer.
        var rows = Build(null, N(0, null), N(1, 0), N(2, 1), N(3, 2), N(4, 2), N(5, 1), N(6, 5), N(7, 6), N(8, 7));
        var fold = rows.Single(r => r.Fold != null);
        Assert.Equal([2, 3, 4], fold.Fold!.Members);
        Assert.Equal([8, 7, 6, 5, 2, 1, 0], rows.Select(r => r.Number));
    }

    [Fact]
    public void RecentBranchesStayOpen()
    {
        var rows = Build(null, N(0, null), N(1, 0), N(2, 1, daysAgo: 2), N(3, 1), N(4, 3));
        Assert.DoesNotContain(rows, r => r.Fold != null);
        Assert.Equal(1, Row(rows, 2).Lane);
    }

    [Fact]
    public void ExpandedParentsShowTheirBranches()
    {
        var rows = Build(new HashSet<int> { 1 }, N(0, null), N(1, 0), N(2, 1), N(3, 1), N(4, 3));
        Assert.DoesNotContain(rows, r => r.Fold != null);
        Assert.Equal(1, Row(rows, 2).Lane);
    }

    [Fact]
    public void SideLanesAreReusedOnceFree()
    {
        // Main line 0-1-3-4-6-7; 2 forks off 1 and 5 off 4: their spans don't overlap.
        var rows = Build(null, N(0, null, 0), N(1, 0, 0), N(2, 1, 0), N(3, 1, 0), N(4, 3, 0), N(5, 4, 0), N(6, 4, 0), N(7, 6, 0));
        Assert.Equal(1, Row(rows, 2).Lane);
        Assert.Equal(1, Row(rows, 5).Lane);
    }

    [Fact]
    public void OverlappingBranchesGetTheirOwnLanes()
    {
        // 2 and 5 both fork off 1, so both lines run down to 1.
        var rows = Build(null, N(0, null, 0), N(1, 0, 0), N(2, 1, 0), N(3, 1, 0), N(4, 3, 0), N(5, 1, 0), N(6, 4, 0), N(7, 6, 0));
        Assert.Equal(0, Row(rows, 7).Lane);
        Assert.Equal(1, Row(rows, 5).Lane);
        Assert.Equal(2, Row(rows, 2).Lane);
        Assert.Equal([1, 2], Row(rows, 1).BranchesIn);
        Assert.Equal([0, 1], Row(rows, 2).PassThrough);
    }

    [Fact]
    public void ARevisionWithAMissingParentStartsItsOwnLine()
    {
        var rows = Build(null, N(0, null, 0), N(1, 0, 0), N(2, 1, 0), N(5, 99, 0));
        var orphan = Row(rows, 5);
        Assert.Equal(1, orphan.Lane);
        Assert.False(orphan.LineDown);
        Assert.Equal(0, Row(rows, 2).Lane);
    }

    [Fact]
    public void ParentLoopsDontHang()
    {
        var rows = Build(null, N(1, 2), N(2, 1));
        Assert.Equal([2, 1], rows.Select(r => r.Number));
    }
}
