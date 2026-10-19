using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using RaiderLite.Core.Tunes;

namespace RaiderLite;

/// <summary>One row of the timeline: a revision, or side branches folded into one line.</summary>
internal sealed class TimelineItem(TimelineRow row, Revision? revision, IReadOnlyList<Revision> members)
{
    public TimelineRow Row { get; } = row;

    /// <summary>The row's revision; null for a fold.</summary>
    public Revision? Revision { get; } = revision;

    /// <summary>The folded revisions, oldest first; empty for a revision row.</summary>
    public IReadOnlyList<Revision> Members { get; } = members;

    public bool IsFold => Row.Fold != null;
}

/// <summary>
/// The car's revisions, newest first, with a git-style graph on the left: the main line straight
/// down lane 0, side branches in further lanes bending into the revision they fork from.
/// </summary>
internal sealed class RevisionTimeline : ListBox
{
    // Lane colours: the main line in the accent blue, side branches in the ribbon's other colours.
    private static readonly Color[] LaneColors =
    [
        Color.FromArgb(0, 103, 192), Color.FromArgb(118, 64, 168), Color.FromArgb(16, 124, 16),
        Color.FromArgb(202, 80, 16), Color.FromArgb(0, 120, 128), Color.FromArgb(160, 40, 110),
    ];
    private static readonly Color SelectedBack = Color.FromArgb(204, 228, 247);
    private static readonly Color SelectedBackInactive = Color.FromArgb(229, 236, 243);
    private static readonly Color Muted = Color.FromArgb(118, 122, 130);
    private static readonly Color Rejected = Color.FromArgb(170, 70, 60);
    private static readonly Font Strikeout = new(UiFonts.Regular, FontStyle.Strikeout);
    private static readonly Font Italic = new(UiFonts.Regular, FontStyle.Italic);

    private readonly ToolTip _tip = new() { InitialDelay = 600, ReshowDelay = 200, AutoPopDelay = 20000 };
    private int _tipIndex = -1;
    private int _lanes = 1;

    public RevisionTimeline()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        SelectionMode = SelectionMode.MultiExtended;
        IntegralHeight = false;
        BorderStyle = BorderStyle.None;
        Font = UiFonts.Regular;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        ItemHeight = Px(24);
    }

    /// <summary>Whether a revision's image is open in the editor (drawn bold, with a ring).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<Revision, bool> IsOpen { get; set; } = _ => false;

    /// <summary>Tooltip text for a row.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<TimelineItem, string> TooltipFor { get; set; } = _ => "";

    /// <summary>A revision row was double-clicked or Enter was pressed on it.</summary>
    public event EventHandler<Revision>? RevisionActivated;

    /// <summary>A folded row was double-clicked or Enter was pressed on it.</summary>
    public event EventHandler<TimelineItem>? FoldActivated;

    public IEnumerable<TimelineItem> SelectedTimelineItems => SelectedItems.Cast<TimelineItem>();

    /// <summary>Replaces the rows, keeping the selection on the same revisions where possible.</summary>
    public void SetItems(IReadOnlyList<TimelineItem> items, IReadOnlyCollection<int>? select = null)
    {
        var keep = select ?? SelectedTimelineItems.Select(Key).ToList();
        int top = TopIndex;
        BeginUpdate();
        try
        {
            Items.Clear();
            _lanes = items.Count == 0 ? 1 : items.Max(i => Math.Max(i.Row.Lane, i.Row.PassThrough.Concat(i.Row.BranchesIn).DefaultIfEmpty(0).Max())) + 1;
            foreach (var item in items)
                Items.Add(item);
            for (int i = 0; i < items.Count; i++)
            {
                if (keep.Contains(Key(items[i])))
                    SetSelected(i, true);
            }
            if (select == null && Items.Count > 0)
                TopIndex = Math.Min(top, Items.Count - 1);
            else if (SelectedIndex >= 0)
                TopIndex = Math.Max(0, SelectedIndex - 3);
        }
        finally
        {
            EndUpdate();
        }
        _tipIndex = -1;
    }

    // Folds are keyed by the revision they fork from, negated so they don't clash with revision numbers.
    private static int Key(TimelineItem item) => item.Row.Fold is { } fold ? -1 - fold.Parent : item.Row.Number;

    private int Px(float v) => (int)Math.Round(v * DeviceDpi / 96f);

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ItemHeight = Px(24);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not TimelineItem item)
            return;
        var g = e.Graphics;
        var bounds = e.Bounds;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? (Focused ? SelectedBack : SelectedBackInactive) : BackColor))
            g.FillRectangle(back, bounds);

        int laneWidth = Px(14);
        int graphLeft = bounds.Left + Px(6);
        float X(int lane) => graphLeft + lane * laneWidth + laneWidth / 2f;
        float top = bounds.Top, bottom = bounds.Bottom, cy = bounds.Top + bounds.Height / 2f;
        var row = item.Row;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        float width = Math.Max(1.5f, Px(2));
        Pen LanePen(int lane) => new(LaneColors[lane % LaneColors.Length], width);
        foreach (var lane in row.PassThrough)
        {
            using var pen = LanePen(lane);
            g.DrawLine(pen, X(lane), top, X(lane), bottom);
        }
        using (var pen = LanePen(row.Lane))
        {
            if (row.LineUp)
                g.DrawLine(pen, X(row.Lane), top, X(row.Lane), cy);
            if (row.LineDown)
                g.DrawLine(pen, X(row.Lane), cy, X(row.Lane), bottom);
        }
        foreach (var lane in row.BranchesIn)
        {
            // A side branch comes down its lane and bends into this revision.
            using var pen = LanePen(lane);
            g.DrawBezier(pen, X(lane), top, X(lane), cy, X(lane), cy, X(row.Lane), cy);
        }
        DrawNode(g, item, X(row.Lane), cy);
        g.SmoothingMode = SmoothingMode.Default;

        int textLeft = graphLeft + _lanes * laneWidth + Px(4);
        int iconSize = Px(16);
        int iconsWidth = iconSize + Px(6);
        var textArea = Rectangle.FromLTRB(textLeft, bounds.Top, bounds.Right - iconsWidth, bounds.Bottom);
        if (item.Revision is { } r)
        {
            DrawRevision(g, r, textArea);
            DrawIcons(g, r, Rectangle.FromLTRB(bounds.Right - iconsWidth, bounds.Top, bounds.Right - Px(4), bounds.Bottom), iconSize);
        }
        else
        {
            DrawFold(g, item, textArea);
        }
        if ((e.State & DrawItemState.Focus) != 0 && Focused)
            ControlPaint.DrawFocusRectangle(g, bounds);
    }

    private void DrawNode(Graphics g, TimelineItem item, float x, float y)
    {
        var color = LaneColors[item.Row.Lane % LaneColors.Length];
        float r = Px(4.5f);
        var circle = new RectangleF(x - r, y - r, 2 * r, 2 * r);
        if (item.Revision is not { } revision)
        {
            // A folded branch: a small stack of nodes.
            using var brush = new SolidBrush(BackColor);
            using var pen = new Pen(color, Math.Max(1.5f, Px(1.5f)));
            var behind = circle;
            behind.Offset(0, -Px(3));
            g.FillEllipse(brush, behind);
            g.DrawEllipse(pen, behind);
            g.FillEllipse(brush, circle);
            g.DrawEllipse(pen, circle);
            return;
        }
        bool muted = revision.Hidden || revision.Rejected != null;
        bool noChange = revision.Car.IsNoChange(revision);
        using (var outline = new Pen(muted ? Muted : color, Math.Max(1.5f, Px(1.5f))))
        {
            if (noChange || muted)
            {
                using var fill = new SolidBrush(BackColor);
                g.FillEllipse(fill, circle);
            }
            else
            {
                using var fill = new SolidBrush(color);
                g.FillEllipse(fill, circle);
            }
            g.DrawEllipse(outline, circle);
        }
        if (IsOpen(revision))
        {
            // An open image gets a ring around its node.
            float ring = r + Px(2.5f);
            using var pen = new Pen(color, Math.Max(1f, Px(1f)));
            g.DrawEllipse(pen, x - ring, y - ring, 2 * ring, 2 * ring);
        }
    }

    private void DrawRevision(Graphics g, Revision revision, Rectangle area)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        bool open = IsOpen(revision);
        bool rejected = revision.Rejected != null;
        bool noChange = revision.Car.IsNoChange(revision);
        var numberFont = open ? UiFonts.Bold : UiFonts.Regular;
        int numberWidth = TextRenderer.MeasureText(g, "0000", UiFonts.Bold, Size.Empty, TextFormatFlags.NoPadding).Width;
        int dateWidth = TextRenderer.MeasureText(g, "30 Mmm 88", UiFonts.Regular, Size.Empty, TextFormatFlags.NoPadding).Width + Px(8);

        var number = new Rectangle(area.Left, area.Top, numberWidth, area.Height);
        TextRenderer.DrawText(g, revision.Number.ToString("000", CultureInfo.InvariantCulture), numberFont, number, Muted, flags);
        var date = new Rectangle(number.Right, area.Top, dateWidth, area.Height);
        TextRenderer.DrawText(g, ShortDate(revision.Created), UiFonts.Regular, date, Muted, flags);

        var labelArea = Rectangle.FromLTRB(date.Right, area.Top, area.Right, area.Bottom);
        var label = revision.Label.Length > 0 ? revision.Label : "(no label)";
        var font = rejected ? Strikeout : revision.Hidden ? Italic : open ? UiFonts.Bold : UiFonts.Regular;
        var color = rejected ? Rejected : revision.Hidden || noChange || revision.Label.Length == 0 ? Muted : ForeColor;
        if (noChange)
            label += "  · no change";
        TextRenderer.DrawText(g, label, font, labelArea, color, flags);
    }

    private void DrawFold(Graphics g, TimelineItem item, Rectangle area)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        var members = item.Members;
        var range = members.Count == 1
            ? $"{members[0].Number:000}"
            : $"{members[0].Number:000}–{members[^1].Number:000}";
        var labels = string.Join(", ", members.Select(m => m.Label).Where(l => l.Length > 0).Distinct());
        var text = $"▸ {range}  {members.Count} folded" + (labels.Length > 0 ? $": {labels}" : "");
        TextRenderer.DrawText(g, text, Italic, area, Muted, flags);
    }

    private void DrawIcons(Graphics g, Revision revision, Rectangle area, int size)
    {
        if (revision.Car.IsLocked(revision))
            g.DrawImage(IconFont.Render(IconFont.Lock, size, Muted), area.Right - size, area.Top + (area.Height - size) / 2);
    }

    internal static string ShortDate(DateTime when) =>
        when.Year == DateTime.Now.Year
            ? when.ToString("d MMM", CultureInfo.CurrentCulture)
            : when.ToString("d MMM yy", CultureInfo.CurrentCulture);

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        int index = IndexFromPoint(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0 && Items[index] is TimelineItem item)
            Activate(item);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter && SelectedItems.Count == 1 && SelectedItem is TimelineItem item)
        {
            Activate(item);
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    private void Activate(TimelineItem item)
    {
        if (item.Revision is { } r)
            RevisionActivated?.Invoke(this, r);
        else
            FoldActivated?.Invoke(this, item);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        // Right-click selects the row under the mouse (unless it is part of the selection) before the menu opens.
        if (e.Button == MouseButtons.Right)
        {
            int index = IndexFromPoint(e.Location);
            if (index >= 0 && !GetSelected(index))
            {
                ClearSelected();
                SetSelected(index, true);
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexFromPoint(e.Location);
        if (index == _tipIndex)
            return;
        _tipIndex = index;
        _tip.SetToolTip(this, index >= 0 && Items[index] is TimelineItem item ? TooltipFor(item) : "");
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _tipIndex = -1;
        _tip.SetToolTip(this, "");
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _tip.Dispose();
        base.Dispose(disposing);
    }
}
