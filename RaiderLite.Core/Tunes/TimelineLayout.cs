namespace RaiderLite.Core.Tunes;

/// <summary>A revision as the timeline sees it: only what affects the layout.</summary>
public sealed record TimelineNode(int Number, int? Parent, DateTime Created);

/// <summary>
/// Side branches folded into one row: the revision they fork from (the key to expand them by)
/// and every revision in them, by number.
/// </summary>
public sealed record TimelineFold(int Parent, IReadOnlyList<int> Members);

/// <summary>
/// One row of the timeline, newest first. The row's node sits in <see cref="Lane"/> (0 is the
/// main line). Lines: <see cref="LineUp"/> and <see cref="LineDown"/> continue the node's own
/// lane above and below it; <see cref="PassThrough"/> lanes cross the row; each lane in
/// <see cref="BranchesIn"/> comes down from above and bends into the node (a side branch ending at
/// its parent).
/// </summary>
public sealed record TimelineRow(
    int Number,
    int Lane,
    bool LineUp,
    bool LineDown,
    IReadOnlyList<int> PassThrough,
    IReadOnlyList<int> BranchesIn,
    TimelineFold? Fold = null);

/// <summary>
/// Lays out revisions as a git-style graph, newest first. The main line runs from the root to the
/// newest revision (at every fork, through the child whose branch holds the newest revision), so
/// the line being worked on stays straight in lane 0 however long the old side branches are; other
/// children start side branches in further lanes, which are reused once
/// free. Side branches that are finished (nothing in them newer than <c>foldAfter</c>) fold, all
/// those of one parent together, into one row.
/// </summary>
public static class TimelineLayout
{
    public static readonly TimeSpan DefaultFoldAfter = TimeSpan.FromDays(7);

    private sealed class Branch
    {
        public required List<int> Chain;          // oldest to newest, each the parent of the next
        public int? Parent;                        // where it forks off; null for a root
        public readonly List<Branch> Children = []; // side branches forking off this one
        public int Lane;
    }

    /// <summary>
    /// Lays out <paramref name="nodes"/>. A parent that isn't among them makes a node a root.
    /// <paramref name="expanded"/> holds parent numbers whose folded branches are shown in full.
    /// </summary>
    public static IReadOnlyList<TimelineRow> Build(
        IReadOnlyCollection<TimelineNode> nodes, DateTime now, IReadOnlySet<int>? expanded = null, TimeSpan? foldAfter = null)
    {
        if (nodes.Count == 0)
            return [];
        var byNumber = nodes.ToDictionary(n => n.Number);
        int? ParentOf(int n) => byNumber[n].Parent is int p && p != n && byNumber.ContainsKey(p) ? p : null;

        var children = nodes.ToDictionary(n => n.Number, _ => new List<int>());
        foreach (var n in nodes)
        {
            if (ParentOf(n.Number) is int p)
                children[p].Add(n.Number);
        }
        foreach (var list in children.Values)
            list.Sort();

        // Subtree sizes and newest members, iteratively (histories can be long) and safe against loops.
        var size = new Dictionary<int, int>();
        var newest = new Dictionary<int, int>();
        foreach (var start in nodes.Select(n => n.Number))
        {
            var stack = new Stack<(int Node, bool Done)>();
            stack.Push((start, false));
            var onPath = new HashSet<int>();
            while (stack.Count > 0)
            {
                var (node, done) = stack.Pop();
                if (size.ContainsKey(node))
                    continue;
                if (done)
                {
                    size[node] = 1 + children[node].Sum(c => size.GetValueOrDefault(c));
                    newest[node] = children[node].Select(c => newest.GetValueOrDefault(c, c)).Append(node).Max();
                    onPath.Remove(node);
                    continue;
                }
                if (!onPath.Add(node))
                    continue;
                stack.Push((node, true));
                foreach (var c in children[node].Where(c => !size.ContainsKey(c) && !onPath.Contains(c)))
                    stack.Push((c, false));
            }
        }

        // Each branch follows the child leading to its newest revision.
        int Next(List<int> options) => options.OrderByDescending(c => newest[c]).ThenByDescending(c => size[c]).First();

        var placed = new HashSet<int>();
        Branch MakeBranch(int first, int? parent)
        {
            var branch = new Branch { Chain = [], Parent = parent };
            for (int? n = first; n is int k && placed.Add(k); n = children[k].Count > 0 ? Next(children[k]) : null)
                branch.Chain.Add(k);
            foreach (var member in branch.Chain)
            {
                foreach (var side in children[member].Where(c => !placed.Contains(c)))
                    branch.Children.Add(MakeBranch(side, member));
            }
            return branch;
        }

        var roots = nodes.Select(n => n.Number).Where(n => ParentOf(n) == null)
            .OrderByDescending(n => size[n]).ThenBy(n => n).ToList();
        var main = MakeBranch(roots.Count > 0 ? roots[0] : nodes.Min(n => n.Number), null);
        var extraRoots = roots.Skip(1).Where(r => !placed.Contains(r)).Select(r => MakeBranch(r, null)).ToList();
        // Nodes caught in a parent loop are reachable from no root; give each loop its own line.
        foreach (var n in nodes.Select(n => n.Number).Order().Where(n => !placed.Contains(n)).ToList())
        {
            if (!placed.Contains(n))
                extraRoots.Add(MakeBranch(n, null));
        }

        // ----- folding -----
        var cutoff = now - (foldAfter ?? DefaultFoldAfter);
        IEnumerable<int> Members(Branch b) => b.Chain.Concat(b.Children.SelectMany(Members));
        bool Finished(Branch b) => Members(b).All(n => byNumber[n].Created < cutoff);

        // Folds by parent number; the branches folded into each.
        var folds = new Dictionary<int, List<Branch>>();
        var visible = new List<Branch>();   // branches drawn in full (not folded)
        void Collect(Branch b)
        {
            visible.Add(b);
            foreach (var group in b.Children.GroupBy(c => c.Parent!.Value))
            {
                bool open = expanded?.Contains(group.Key) == true;
                foreach (var child in group)
                {
                    if (!open && Finished(child))
                        (folds.TryGetValue(group.Key, out var list) ? list : folds[group.Key] = []).Add(child);
                    else
                        Collect(child);
                }
            }
        }
        Collect(main);
        foreach (var r in extraRoots)
            Collect(r);

        // ----- rows: one per visible node and one per fold, newest first -----
        var foldMembers = folds.ToDictionary(f => f.Key, f => f.Value.SelectMany(Members).Order().ToList());
        var items = visible.SelectMany(b => b.Chain.Select(n => (Key: n, Fold: (int?)null)))
            .Concat(foldMembers.Select(f => (Key: f.Value[0], Fold: (int?)f.Key)))
            .OrderByDescending(i => i.Key)
            .ToList();
        var rowOfNode = new Dictionary<int, int>();
        var rowOfFold = new Dictionary<int, int>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Fold is int parent)
                rowOfFold[parent] = i;
            else
                rowOfNode[items[i].Key] = i;
        }

        // ----- lanes: main line 0; side spans take the lowest free lane -----
        // A span covers rows top..bottom: from its newest row down to the parent's row it bends into.
        var spans = new List<(int Top, int Bottom, Action<int> Assign)>();
        foreach (var b in visible.Where(b => !ReferenceEquals(b, main)))
        {
            int top = b.Chain.Min(n => rowOfNode[n]);
            int bottom = b.Parent is int p ? rowOfNode[p] : b.Chain.Max(n => rowOfNode[n]);
            spans.Add((top, bottom, lane => b.Lane = lane));
        }
        var foldLane = new Dictionary<int, int>();
        foreach (var (parent, row) in rowOfFold)
            spans.Add((row, rowOfNode[parent], lane => foldLane[parent] = lane));
        var laneEnds = new List<int>();     // per side lane (index + 1), the last row it occupies
        foreach (var span in spans.OrderBy(s => s.Top).ThenBy(s => s.Bottom))
        {
            int lane = laneEnds.FindIndex(end => end < span.Top);
            if (lane < 0)
            {
                laneEnds.Add(span.Bottom);
                lane = laneEnds.Count - 1;
            }
            else
            {
                laneEnds[lane] = span.Bottom;
            }
            span.Assign(lane + 1);
        }

        // ----- lines -----
        var pass = items.Select(_ => new SortedSet<int>()).ToArray();
        var into = items.Select(_ => new SortedSet<int>()).ToArray();
        var lineUp = new bool[items.Count];
        var lineDown = new bool[items.Count];
        var laneOfRow = new int[items.Count];

        var laneOfNode = new Dictionary<int, int>();
        foreach (var b in visible)
        {
            foreach (var n in b.Chain)
                laneOfNode[n] = b.Lane;
        }
        for (int i = 0; i < items.Count; i++)
            laneOfRow[i] = items[i].Fold is int fp ? foldLane[fp] : laneOfNode[items[i].Key];

        // A line in a lane from one row down to another: it crosses the rows in between (lanes are
        // allocated so no other node sits in it there) and, for a side branch, bends into the parent.
        void Span(int lane, int fromRow, int toRow, bool bendsIn)
        {
            for (int r = fromRow + 1; r < toRow; r++)
            {
                if (laneOfRow[r] != lane)
                    pass[r].Add(lane);
            }
            if (bendsIn)
                into[toRow].Add(lane);
        }

        foreach (var b in visible)
        {
            var rows = b.Chain.Select(n => rowOfNode[n]).ToList();   // oldest first, so descending rows
            for (int i = 0; i < rows.Count; i++)
            {
                lineUp[rows[i]] = i < rows.Count - 1;
                lineDown[rows[i]] = i > 0 || b.Parent != null;
                if (i > 0)
                    Span(b.Lane, Math.Min(rows[i], rows[i - 1]), Math.Max(rows[i], rows[i - 1]), bendsIn: false);
            }
            if (b.Parent is int p)
                Span(b.Lane, rows[0], rowOfNode[p], bendsIn: true);
        }
        foreach (var (parent, row) in rowOfFold)
        {
            lineDown[row] = true;
            Span(foldLane[parent], row, rowOfNode[parent], bendsIn: true);
        }

        return items.Select((item, i) => new TimelineRow(
            item.Key,
            laneOfRow[i],
            lineUp[i],
            lineDown[i],
            pass[i].ToList(),
            into[i].ToList(),
            item.Fold is int f ? new TimelineFold(f, foldMembers[f]) : null)).ToList();
    }

    /// <summary>
    /// The car's revisions as timeline nodes. Unless <paramref name="showHidden"/>, hidden and
    /// rejected revisions are left out and their children attach to the nearest shown ancestor.
    /// </summary>
    public static IReadOnlyList<TimelineNode> NodesFor(Car car, bool showHidden)
    {
        bool Shown(Revision r) => showHidden || !r.Hidden && r.Rejected == null;
        var nodes = new List<TimelineNode>();
        foreach (var r in car.Revisions.Where(Shown))
        {
            var parent = car.ParentOf(r);
            var seen = new HashSet<Revision>();
            while (parent != null && !Shown(parent) && seen.Add(parent))
                parent = car.ParentOf(parent);
            nodes.Add(new TimelineNode(r.Number, parent != null && Shown(parent) ? parent.Number : null, r.Created));
        }
        return nodes;
    }
}
