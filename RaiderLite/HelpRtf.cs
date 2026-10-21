using System.Globalization;
using System.Text;
using RaiderLite.Core.Help;

namespace RaiderLite;

/// <summary>
/// Writes a help page as RTF for a RichTextBox: headings, paragraphs, lists, tables, notes and
/// code, with links as HYPERLINK fields. Tables are laid out for a given width, so a page is
/// rendered again when the window's width changes.
/// </summary>
internal static class HelpRtf
{
    // Colour table indices (see Header).
    private const int TextColor = 1;
    private const int AccentColor = 2;
    private const int CodeBack = 3;
    private const int NoteBack = 4;
    private const int HeaderBack = 5;
    private const int BorderColor = 6;
    private const int NoteText = 7;

    private const string Header =
        @"{\rtf1\ansi\ansicpg1252\deff0\deflang1033" +
        @"{\fonttbl{\f0\fswiss\fcharset0 Segoe UI;}{\f1\fmodern\fcharset0 Consolas;}}" +
        @"{\colortbl ;\red32\green33\blue36;\red0\green95\blue184;\red238\green240\blue243;\red255\green246\blue219;\red234\green237\blue241;\red208\green212\blue218;\red102\green72\blue0;}" +
        @"\viewkind4\uc1\f0\fs20\cf1" + "\n";

    // Body text 10 pt, code 9.5 pt; line spacing 1.2.
    private const string Body = @"\f0\fs20\cf1\sl288\slmult1";
    private const int CellGapTwips = 110;

    public static readonly Font BodyFont = new("Segoe UI", 10f);
    public static readonly Font BodyBoldFont = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font CodeFont = new("Consolas", 9.5f);

    /// <summary>The page as RTF, with tables fitted to <paramref name="widthTwips"/>.</summary>
    public static string Render(HelpTopic topic, int widthTwips)
    {
        var sb = new StringBuilder(Header);
        foreach (var block in topic.Blocks)
        {
            switch (block)
            {
                case HelpHeading { Level: 1 } h:
                    sb.Append(@"\pard\sb40\sa220{\fs36\b\cf2 ");
                    Escape(sb, h.Text);
                    sb.Append(@"}\par").Append('\n');
                    break;
                case HelpHeading { Level: 2 } h:
                    sb.Append(@"\pard\keepn\sb300\sa100{\fs27\b ");
                    Escape(sb, h.Text);
                    sb.Append(@"}\par").Append('\n');
                    break;
                case HelpHeading h:
                    sb.Append(@"\pard\keepn\sb200\sa80{\fs22\b ");
                    Escape(sb, h.Text);
                    sb.Append(@"}\par").Append('\n');
                    break;
                case HelpParagraph p:
                    sb.Append(@"\pard").Append(Body).Append(@"\sa160 ");
                    Runs(sb, p.Runs);
                    sb.Append(@"\par").Append('\n');
                    break;
                case HelpList l:
                    for (int i = 0; i < l.Items.Count; i++)
                    {
                        bool last = i == l.Items.Count - 1;
                        sb.Append(@"\pard").Append(Body).Append(@"\fi-300\li600\tx600").Append(last ? @"\sa160 " : @"\sa70 ");
                        sb.Append(l.Numbered ? (i + 1).ToString(CultureInfo.InvariantCulture) + "." : @"\bullet").Append(@"\tab ");
                        Runs(sb, l.Items[i]);
                        sb.Append(@"\par").Append('\n');
                    }
                    break;
                case HelpNote n:
                    Box(sb, widthTwips, NoteBack, leftBar: true, () =>
                    {
                        sb.Append(@"\cf").Append(NoteText).Append(' ');
                        Runs(sb, n.Runs);
                    });
                    break;
                case HelpCode c:
                    Box(sb, widthTwips, CodeBack, leftBar: false, () =>
                    {
                        sb.Append(@"\f1\fs19\sl0 ");
                        var lines = c.Text.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (i > 0)
                                sb.Append(@"\line ");
                            Escape(sb, lines[i]);
                        }
                    });
                    break;
                case HelpTable t:
                    Table(sb, t, widthTwips);
                    break;
            }
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void Runs(StringBuilder sb, IEnumerable<HelpRun> runs)
    {
        foreach (var run in runs)
        {
            if (run.Link != null)
            {
                sb.Append(@"{\field{\*\fldinst{HYPERLINK """);
                Escape(sb, run.Link);
                sb.Append(@"""}}{\fldrslt{\cf2\ul ");
            }
            sb.Append('{');
            if (run.Style.HasFlag(HelpRunStyle.Bold))
                sb.Append(@"\b");
            if (run.Style.HasFlag(HelpRunStyle.Code))
                sb.Append(@"\f1\fs19\highlight").Append(CodeBack);
            // A space ends a control word; after a bare "{" it would be text.
            if (run.Style != HelpRunStyle.None)
                sb.Append(' ');
            Escape(sb, run.Text);
            sb.Append('}');
            if (run.Link != null)
                sb.Append("}}}");
        }
    }

    /// <summary>A one-cell shaded table holding a note or code, as wide as the page.</summary>
    private static void Box(StringBuilder sb, int widthTwips, int back, bool leftBar, Action content)
    {
        sb.Append(@"\trowd\trgaph160\trleft0");
        sb.Append(@"\clcbpat").Append(back);
        if (leftBar)
            sb.Append(@"\clbrdrl\brdrs\brdrw40\brdrcf").Append(NoteText);
        sb.Append(@"\cellx").Append(widthTwips).Append('\n');
        sb.Append(@"\pard\intbl").Append(Body).Append(@"\sb90\sa90 ");
        content();
        sb.Append(@"\cell\row").Append('\n');
        Spacer(sb);
    }

    private static void Table(StringBuilder sb, HelpTable table, int widthTwips)
    {
        int columns = Math.Max(table.Header.Count, table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Count));
        if (columns == 0)
            return;
        var widths = ColumnWidths(table, columns, widthTwips);
        var rows = table.Rows.Prepend(table.Header).ToList();
        for (int r = 0; r < rows.Count; r++)
        {
            sb.Append(@"\trowd\trgaph").Append(CellGapTwips).Append(@"\trleft0");
            int right = 0;
            for (int c = 0; c < columns; c++)
            {
                right += widths[c];
                if (r == 0)
                    sb.Append(@"\clcbpat").Append(HeaderBack);
                foreach (var side in new[] { "t", "l", "b", "r" })
                    sb.Append(@"\clbrdr").Append(side).Append(@"\brdrs\brdrw10\brdrcf").Append(BorderColor);
                sb.Append(@"\cellx").Append(right);
            }
            sb.Append('\n');
            for (int c = 0; c < columns; c++)
            {
                sb.Append(@"\pard\intbl").Append(Body).Append(@"\sb50\sa50 ");
                if (c < rows[r].Count)
                {
                    if (r == 0)
                        sb.Append(@"{\b ");
                    Runs(sb, rows[r][c]);
                    if (r == 0)
                        sb.Append('}');
                }
                sb.Append(@"\cell ");
            }
            sb.Append(@"\row").Append('\n');
        }
        Spacer(sb);
    }

    /// <summary>A small gap after a table (RichEdit needs a paragraph after one anyway).</summary>
    private static void Spacer(StringBuilder sb) => sb.Append(@"\pard\sa0\sl0\fs10\par\fs20").Append('\n');

    /// <summary>
    /// Column widths as a browser's automatic table layout would give them: each column as wide
    /// as its longest cell if they all fit; otherwise each at least as wide as its longest word,
    /// with the rest of the width shared in proportion to how much more each column wants.
    /// </summary>
    private static int[] ColumnWidths(HelpTable table, int columns, int widthTwips)
    {
        var natural = new int[columns];
        var minimum = new int[columns];
        int pad = 2 * CellGapTwips + 60;
        foreach (var (row, isHeader) in table.Rows.Select(r => (r, false)).Prepend((table.Header, true)))
        {
            var font = isHeader ? BodyBoldFont : BodyFont;
            for (int c = 0; c < row.Count && c < columns; c++)
            {
                var text = string.Concat(row[c].Select(run => run.Text));
                natural[c] = Math.Max(natural[c], MeasureTwips(text, font) + pad);
                foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    minimum[c] = Math.Max(minimum[c], MeasureTwips(word, font) + pad);
            }
        }
        if (natural.Sum() <= widthTwips)
            return natural;
        int spare = widthTwips - minimum.Sum();
        if (spare <= 0)
            return minimum;
        double wanted = natural.Zip(minimum, (n, m) => (double)(n - m)).Sum();
        return minimum.Select((m, c) => m + (int)(spare * (natural[c] - m) / Math.Max(1, wanted))).ToArray();
    }

    private static int MeasureTwips(string text, Font font)
    {
        var size = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return size.Width * 1440 / ScreenDpi;
    }

    private static readonly int ScreenDpi = GetScreenDpi();

    private static int GetScreenDpi()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return (int)Math.Round(g.DpiX);
    }

    /// <summary>Converts pixels at <paramref name="dpi"/> to twips (1/1440 inch).</summary>
    public static int PixelsToTwips(int pixels, int dpi) => pixels * 1440 / Math.Max(1, dpi);

    private static void Escape(StringBuilder sb, string text)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\':
                    sb.Append(@"\\");
                    break;
                case '{':
                    sb.Append(@"\{");
                    break;
                case '}':
                    sb.Append(@"\}");
                    break;
                case '\t':
                    sb.Append(@"\tab ");
                    break;
                case < ' ':
                    break;
                case > '\x7f':
                    sb.Append(@"\u").Append(((short)c).ToString(CultureInfo.InvariantCulture)).Append('?');
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
    }
}
