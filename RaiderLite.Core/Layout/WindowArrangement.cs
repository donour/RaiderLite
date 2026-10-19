using System.Drawing;

namespace RaiderLite.Core.Layout;

/// <summary>
/// Places windows in an area, each starting from its preferred size (a table window's fitted
/// size), so small tables don't get large empty windows and large ones aren't squeezed for no
/// reason. Pure layout: the result is one rectangle per window, in the order given.
/// </summary>
public static class WindowArrangement
{
    /// <summary>
    /// The layout that shows the most of the windows' contents, without overlaps: windows at their
    /// preferred sizes in rows (or shrunk evenly to fit), side by side, stacked, a grid, or the
    /// largest on the left with the others stacked beside it. Scored by the share of each window's
    /// preferred size that is visible; the first candidate wins a tie, so windows that all fit
    /// keep their sizes.
    /// </summary>
    public static IReadOnlyList<Rectangle> Arrange(IReadOnlyList<Size> preferred, Size area, Size minimum)
    {
        if (preferred.Count == 0 || area.Width <= 0 || area.Height <= 0)
            return [];
        var sizes = preferred.Select(s => Clamp(s, minimum, area)).ToList();
        var candidates = new List<IReadOnlyList<Rectangle>>();
        for (double scale = 1.0; scale >= 0.5; scale -= 0.1)
        {
            var scaled = sizes.Select(s => Clamp(new Size((int)(s.Width * scale), (int)(s.Height * scale)), minimum, area)).ToList();
            if (Shelves(scaled, area) is { } placed)
                candidates.Add(placed);
        }
        candidates.Add(SideBySide(sizes, area, minimum));
        candidates.Add(Stacked(sizes, area, minimum));
        for (int columns = 2; columns < sizes.Count; columns++)
        {
            candidates.Add(Grid(sizes, area, columns));
            candidates.Add(Columns(sizes, area, minimum, columns));
        }
        // It needs room for two columns of at least the minimum width.
        if (sizes.Count >= 2 && area.Width >= 2 * minimum.Width)
            candidates.Add(MainAndStack(sizes, area, minimum));
        return candidates.MaxBy(c => Shown(sizes, c))!;
    }

    /// <summary>The share of each window's preferred size that a layout shows, summed.</summary>
    private static double Shown(IReadOnlyList<Size> sizes, IReadOnlyList<Rectangle> rects) =>
        sizes.Zip(rects).Sum(p => Math.Min(p.First.Width, p.Second.Width) * (double)Math.Min(p.First.Height, p.Second.Height)
                                  / Math.Max(1.0, p.First.Width * (double)p.First.Height));

    /// <summary>
    /// <paramref name="count"/> columns of stacked windows: the tallest windows are dealt first, each
    /// to the column with the least height so far; each column is as wide as its widest window
    /// (shared out when they don't fit), and its windows keep their order.
    /// </summary>
    private static IReadOnlyList<Rectangle> Columns(IReadOnlyList<Size> sizes, Size area, Size minimum, int count)
    {
        var members = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        var heights = new long[count];
        foreach (int i in Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i].Height))
        {
            int column = Array.IndexOf(heights, heights.Min());
            members[column].Add(i);
            heights[column] += sizes[i].Height;
        }
        var used = members.Where(m => m.Count > 0).ToList();
        var widths = Share(used.Select(m => m.Max(i => sizes[i].Width)).ToList(), area.Width, minimum.Width);
        var result = new Rectangle[sizes.Count];
        int x = 0;
        for (int c = 0; c < used.Count; c++)
        {
            var column = used[c].Order().ToList();
            var stacked = Stacked(column.Select(i => sizes[i] with { Width = Math.Min(sizes[i].Width, widths[c]) }).ToList(), new Size(widths[c], area.Height), minimum);
            for (int k = 0; k < column.Count; k++)
                result[column[k]] = stacked[k] with { X = stacked[k].X + x };
            x += widths[c];
        }
        return result;
    }

    /// <summary>
    /// The largest window on the left, as wide as it needs (leaving a column), the others stacked
    /// beside it. The area must be at least two minimum widths wide.
    /// </summary>
    private static IReadOnlyList<Rectangle> MainAndStack(IReadOnlyList<Size> sizes, Size area, Size minimum)
    {
        int main = Enumerable.Range(0, sizes.Count).MaxBy(i => sizes[i].Width * (long)sizes[i].Height);
        int width = Math.Clamp(sizes[main].Width, minimum.Width, area.Width - minimum.Width);
        var others = Enumerable.Range(0, sizes.Count).Where(i => i != main).ToList();
        var stacked = Stacked(others.Select(i => sizes[i]).ToList(), new Size(area.Width - width, area.Height), minimum);
        var result = new Rectangle[sizes.Count];
        result[main] = new Rectangle(0, 0, width, Math.Min(sizes[main].Height, area.Height));
        for (int k = 0; k < others.Count; k++)
            result[others[k]] = stacked[k] with { X = stacked[k].X + width };
        return result;
    }

    /// <summary>
    /// Side by side: one column per window, as wide as it prefers; when the total is wider than
    /// the area the columns shrink in proportion (equal widths if even that doesn't fit). Each is
    /// as tall as it prefers, up to the area's height.
    /// </summary>
    public static IReadOnlyList<Rectangle> SideBySide(IReadOnlyList<Size> preferred, Size area, Size minimum)
    {
        var widths = Share(preferred.Select(s => s.Width).ToList(), area.Width, minimum.Width);
        var result = new List<Rectangle>();
        int x = 0;
        for (int i = 0; i < preferred.Count; i++)
        {
            int height = Math.Clamp(preferred[i].Height, Math.Min(minimum.Height, area.Height), area.Height);
            result.Add(new Rectangle(x, 0, widths[i], height));
            x += widths[i];
        }
        return result;
    }

    /// <summary>Stacked: one row per window, as for <see cref="SideBySide"/> with the axes swapped.</summary>
    public static IReadOnlyList<Rectangle> Stacked(IReadOnlyList<Size> preferred, Size area, Size minimum)
    {
        var heights = Share(preferred.Select(s => s.Height).ToList(), area.Height, minimum.Height);
        var result = new List<Rectangle>();
        int y = 0;
        for (int i = 0; i < preferred.Count; i++)
        {
            int width = Math.Clamp(preferred[i].Width, Math.Min(minimum.Width, area.Width), area.Width);
            result.Add(new Rectangle(0, y, width, heights[i]));
            y += heights[i];
        }
        return result;
    }

    /// <summary>
    /// Cascade: each window at its preferred size (no larger than the area left after the
    /// offsets), each <paramref name="step"/> right of and below the one before, starting over at
    /// the top left when a window would run out of the area.
    /// </summary>
    public static IReadOnlyList<Rectangle> Cascade(IReadOnlyList<Size> preferred, Size area, Size minimum, int step)
    {
        var result = new List<Rectangle>();
        int offset = 0;
        foreach (var s in preferred)
        {
            var max = new Size(Math.Max(minimum.Width, area.Width - step * (preferred.Count - 1)), Math.Max(minimum.Height, area.Height - step * (preferred.Count - 1)));
            var size = Clamp(s, minimum, new Size(Math.Min(max.Width, area.Width), Math.Min(max.Height, area.Height)));
            if (offset > 0 && (offset + size.Width > area.Width || offset + size.Height > area.Height))
                offset = 0;
            result.Add(new Rectangle(offset, offset, size.Width, size.Height));
            offset += step;
        }
        return result;
    }

    /// <summary>Rows of windows; null if they run past the bottom.</summary>
    private static List<Rectangle>? Shelves(IReadOnlyList<Size> sizes, Size area)
    {
        var placed = new List<Rectangle>();
        int x = 0, y = 0, rowHeight = 0;
        foreach (var s in sizes)
        {
            if (x > 0 && x + s.Width > area.Width)
            {
                y += rowHeight;
                x = 0;
                rowHeight = 0;
            }
            placed.Add(new Rectangle(x, y, s.Width, s.Height));
            x += s.Width;
            rowHeight = Math.Max(rowHeight, s.Height);
        }
        return y + rowHeight <= area.Height ? placed : null;
    }

    /// <summary>Equal cells in <paramref name="columns"/> columns; each window no larger than it prefers.</summary>
    private static List<Rectangle> Grid(IReadOnlyList<Size> sizes, Size area, int columns)
    {
        int rows = (int)Math.Ceiling(sizes.Count / (double)columns);
        int cellWidth = area.Width / columns, cellHeight = area.Height / rows;
        return sizes.Select((s, i) => new Rectangle(
            i % columns * cellWidth, i / columns * cellHeight,
            Math.Min(s.Width, cellWidth), Math.Min(s.Height, cellHeight))).ToList();
    }

    /// <summary>Lengths that fit in <paramref name="total"/>: as preferred, else in proportion, else equal.</summary>
    private static int[] Share(IReadOnlyList<int> preferred, int total, int minimum)
    {
        var lengths = preferred.Select(p => Math.Clamp(p, Math.Min(minimum, total), Math.Max(1, total))).ToArray();
        long sum = lengths.Sum(l => (long)l);
        if (sum <= total || lengths.Length == 0)
            return lengths;
        double scale = total / (double)sum;
        lengths = lengths.Select(l => Math.Max(minimum, (int)(l * scale))).ToArray();
        if (lengths.Sum(l => (long)l) > total)
            lengths = Enumerable.Repeat(Math.Max(1, total / lengths.Length), lengths.Length).ToArray();
        return lengths;
    }

    private static Size Clamp(Size s, Size minimum, Size area) => new(
        Math.Clamp(s.Width, Math.Min(minimum.Width, area.Width), area.Width),
        Math.Clamp(s.Height, Math.Min(minimum.Height, area.Height), area.Height));
}
