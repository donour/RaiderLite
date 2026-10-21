using System.Text;

namespace RaiderLite.Core.Help;

/// <summary>How a run of text is drawn.</summary>
[Flags]
public enum HelpRunStyle
{
    None = 0,
    Bold = 1,
    Code = 2,
}

/// <summary>A run of text with one style; <see cref="Link"/> makes it a link (see <see cref="HelpLibrary.Resolve"/>).</summary>
public sealed record HelpRun(string Text, HelpRunStyle Style = HelpRunStyle.None, string? Link = null);

/// <summary>A block of a help page.</summary>
public abstract record HelpBlock;

/// <summary>A heading: level 1 is the page title, 2 a section (listed in the contents), 3 a subsection.</summary>
public sealed record HelpHeading(int Level, string Text, string Anchor) : HelpBlock;

public sealed record HelpParagraph(IReadOnlyList<HelpRun> Runs) : HelpBlock;

/// <summary>A note or warning set apart from the text (a "&gt;" line).</summary>
public sealed record HelpNote(IReadOnlyList<HelpRun> Runs) : HelpBlock;

public sealed record HelpList(bool Numbered, IReadOnlyList<IReadOnlyList<HelpRun>> Items) : HelpBlock;

/// <summary>A table: a header row and data rows of cells, each cell a list of runs.</summary>
public sealed record HelpTable(IReadOnlyList<IReadOnlyList<HelpRun>> Header, IReadOnlyList<IReadOnlyList<IReadOnlyList<HelpRun>>> Rows) : HelpBlock;

/// <summary>Preformatted text, shown in a fixed-width font.</summary>
public sealed record HelpCode(string Text) : HelpBlock;

/// <summary>
/// One page of the help, parsed from a small subset of Markdown:
/// <list type="bullet">
/// <item><c># Title</c> (first line), <c>## Section</c>, <c>### Subsection</c>; <c>{#id}</c> after a heading sets its anchor.</item>
/// <item>Paragraphs (consecutive lines), <c>- item</c> and <c>1. item</c> lists (following lines continue an item).</item>
/// <item><c>&gt; note</c> lines, <c>| a | b |</c> tables (the second row is the <c>|---|</c> rule), <c>```</c> fenced code.</item>
/// <item>Inline: <c>**bold**</c>, <c>`code`</c>, <c>[text](target)</c>; a backslash escapes the next character.</item>
/// </list>
/// </summary>
public sealed class HelpTopic
{
    private HelpTopic(string id, string title, IReadOnlyList<HelpBlock> blocks)
    {
        Id = id;
        Title = title;
        Blocks = blocks;
        Sections = blocks.OfType<HelpHeading>().Where(h => h.Level == 2).ToList();
        PlainText = BuildPlainText(blocks);
    }

    /// <summary>The page's name in links: its file name without the order prefix ("01-") and extension.</summary>
    public string Id { get; }

    public string Title { get; }

    public IReadOnlyList<HelpBlock> Blocks { get; }

    /// <summary>The level-2 headings, in order: the page's entries in the contents.</summary>
    public IReadOnlyList<HelpHeading> Sections { get; }

    /// <summary>The page's text without formatting, for searching.</summary>
    public string PlainText { get; }

    /// <summary>Every link target on the page, in order.</summary>
    public IEnumerable<string> Links => AllRuns().Where(r => r.Link != null).Select(r => r.Link!);

    /// <summary>The anchors of the page's headings.</summary>
    public IEnumerable<string> Anchors => Blocks.OfType<HelpHeading>().Select(h => h.Anchor);

    /// <summary>The heading with <paramref name="anchor"/>, if any.</summary>
    public HelpHeading? FindHeading(string anchor) =>
        Blocks.OfType<HelpHeading>().FirstOrDefault(h => string.Equals(h.Anchor, anchor, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every run of text on the page, in order (headings and code aside).</summary>
    public IEnumerable<HelpRun> AllRuns()
    {
        foreach (var block in Blocks)
        {
            IEnumerable<HelpRun> runs = block switch
            {
                HelpParagraph p => p.Runs,
                HelpNote n => n.Runs,
                HelpList l => l.Items.SelectMany(i => i),
                HelpTable t => t.Header.Concat(t.Rows.SelectMany(r => r)).SelectMany(c => c),
                _ => [],
            };
            foreach (var run in runs)
                yield return run;
        }
    }

    private static string BuildPlainText(IEnumerable<HelpBlock> blocks)
    {
        var sb = new StringBuilder();
        static string Text(IEnumerable<HelpRun> runs) => string.Concat(runs.Select(r => r.Text));
        foreach (var block in blocks)
        {
            switch (block)
            {
                case HelpHeading h:
                    sb.AppendLine(h.Text);
                    break;
                case HelpParagraph p:
                    sb.AppendLine(Text(p.Runs));
                    break;
                case HelpNote n:
                    sb.AppendLine(Text(n.Runs));
                    break;
                case HelpList l:
                    foreach (var item in l.Items)
                        sb.AppendLine(Text(item));
                    break;
                case HelpTable t:
                    foreach (var row in t.Rows.Prepend(t.Header))
                        sb.AppendLine(string.Join("\t", row.Select(Text)));
                    break;
                case HelpCode c:
                    sb.AppendLine(c.Text);
                    break;
            }
        }
        return sb.ToString();
    }

    // ----- parsing -----

    /// <summary>Parses a page. <paramref name="id"/> names it in links.</summary>
    /// <exception cref="FormatException">The page doesn't start with a "# Title" line.</exception>
    public static HelpTopic Parse(string id, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<HelpBlock>();
        var paragraph = new List<string>();
        var note = new List<string>();
        var items = new List<StringBuilder>();
        bool numbered = false;
        var tableRows = new List<List<string>>();

        void Flush()
        {
            if (paragraph.Count > 0)
                blocks.Add(new HelpParagraph(ParseInline(string.Join(" ", paragraph))));
            if (note.Count > 0)
                blocks.Add(new HelpNote(ParseInline(string.Join(" ", note))));
            if (items.Count > 0)
                blocks.Add(new HelpList(numbered, items.Select(i => (IReadOnlyList<HelpRun>)ParseInline(i.ToString())).ToList()));
            if (tableRows.Count > 0)
            {
                var cells = tableRows.Select(r => (IReadOnlyList<IReadOnlyList<HelpRun>>)r.Select(c => (IReadOnlyList<HelpRun>)ParseInline(c)).ToList()).ToList();
                blocks.Add(new HelpTable(cells[0], cells.Skip(1).ToList()));
            }
            paragraph.Clear();
            note.Clear();
            items.Clear();
            tableRows.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
                    code.Add(lines[i].TrimEnd());
                blocks.Add(new HelpCode(string.Join("\n", code)));
                continue;
            }
            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }
            if (HeadingLevel(line) is int level)
            {
                Flush();
                var (text, anchor) = SplitAnchor(line[(level + 1)..].Trim());
                blocks.Add(new HelpHeading(level, Unescape(text), anchor ?? MakeAnchor(text)));
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                if (note.Count == 0)
                    Flush();
                note.Add(trimmed[1..].Trim());
                continue;
            }
            if (trimmed.StartsWith('|'))
            {
                if (tableRows.Count == 0)
                    Flush();
                var cells = SplitRow(trimmed);
                // The rule under the header row.
                if (tableRows.Count == 1 && cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':')))
                    continue;
                tableRows.Add(cells);
                continue;
            }
            if (ListItem(line) is { } item)
            {
                if (items.Count == 0)
                {
                    Flush();
                    numbered = item.Numbered;
                }
                items.Add(new StringBuilder(item.Text));
                continue;
            }
            // Other lines continue a note or list item (a blank line ends them), as in Markdown.
            if (note.Count > 0)
            {
                note.Add(trimmed);
                continue;
            }
            if (items.Count > 0)
            {
                items[^1].Append(' ').Append(trimmed);
                continue;
            }
            if (tableRows.Count > 0)
                Flush();
            paragraph.Add(trimmed);
        }
        Flush();

        if (blocks.FirstOrDefault() is not HelpHeading { Level: 1 } title)
            throw new FormatException($"Help page {id} must start with a \"# Title\" line.");
        return new HelpTopic(id, title.Text, blocks);
    }

    private static int? HeadingLevel(string line)
    {
        int level = 0;
        while (level < line.Length && line[level] == '#')
            level++;
        return level is >= 1 and <= 3 && level < line.Length && line[level] == ' ' ? level : null;
    }

    private static (string Text, string? Anchor) SplitAnchor(string heading)
    {
        if (heading.EndsWith('}') && heading.LastIndexOf("{#", StringComparison.Ordinal) is int open and > 0)
            return (heading[..open].TrimEnd(), heading[(open + 2)..^1].Trim());
        return (heading, null);
    }

    /// <summary>"Copy and paste" → "copy-and-paste": lower case, runs of other characters become one hyphen.</summary>
    public static string MakeAnchor(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in Unescape(text).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        return sb.ToString().TrimEnd('-');
    }

    private static (bool Numbered, string Text)? ListItem(string line)
    {
        var t = line.TrimStart();
        // Only items at the left margin start a new item; deeper ones continue the last.
        if (line.Length - t.Length > 1)
            return null;
        if (t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal))
            return (false, t[2..].Trim());
        int digits = 0;
        while (digits < t.Length && char.IsAsciiDigit(t[digits]))
            digits++;
        if (digits > 0 && digits + 1 < t.Length && t[digits] == '.' && t[digits + 1] == ' ')
            return (true, t[(digits + 2)..].Trim());
        return null;
    }

    /// <summary>The cells of a "| a | b |" row; "\|" is a bar inside a cell.</summary>
    private static List<string> SplitRow(string row)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        // Skip the leading bar.
        for (int i = 1; i < row.Length; i++)
        {
            if (row[i] == '\\' && i + 1 < row.Length)
            {
                cell.Append(row[i]).Append(row[i + 1]);
                i++;
            }
            else if (row[i] == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(row[i]);
            }
        }
        if (cell.ToString().Trim().Length > 0)
            cells.Add(cell.ToString().Trim());
        return cells;
    }

    private static string Unescape(string text)
    {
        if (!text.Contains('\\'))
            return text;
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
                i++;
            sb.Append(text[i]);
        }
        return sb.ToString();
    }

    /// <summary>Splits a line into runs: <c>**bold**</c>, <c>`code`</c> and <c>[text](target)</c>.</summary>
    public static List<HelpRun> ParseInline(string text)
    {
        var runs = new List<HelpRun>();
        var sb = new StringBuilder();
        bool bold = false;
        var style = () => bold ? HelpRunStyle.Bold : HelpRunStyle.None;

        void Emit(HelpRunStyle s, string? link = null)
        {
            if (sb.Length == 0)
                return;
            // Neighbours with the same style and link join up.
            if (runs.Count > 0 && runs[^1].Style == s && runs[^1].Link == link)
                runs[^1] = runs[^1] with { Text = runs[^1].Text + sb };
            else
                runs.Add(new HelpRun(sb.ToString(), s, link));
            sb.Clear();
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                sb.Append(text[++i]);
            }
            else if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                Emit(style());
                bold = !bold;
                i++;
            }
            else if (c == '`' && text.IndexOf('`', i + 1) is int end and > 0)
            {
                Emit(style());
                sb.Append(text, i + 1, end - i - 1);
                Emit(style() | HelpRunStyle.Code);
                i = end;
            }
            else if (c == '[' && FindLink(text, i) is { } link)
            {
                Emit(style());
                sb.Append(Unescape(link.Text.Replace("**", "")));
                Emit(style(), link.Target);
                i = link.End;
            }
            else
            {
                sb.Append(c);
            }
        }
        Emit(style());
        return runs;
    }

    /// <summary>A "[text](target)" starting at <paramref name="start"/>, and the index of its ")".</summary>
    private static (string Text, string Target, int End)? FindLink(string text, int start)
    {
        int close = text.IndexOf("](", start + 1, StringComparison.Ordinal);
        if (close < 0 || text.IndexOf('[', start + 1, close - start - 1) >= 0)
            return null;
        int end = text.IndexOf(')', close + 2);
        if (end < 0)
            return null;
        var target = text[(close + 2)..end].Trim();
        return target.Length == 0 ? null : (text[(start + 1)..close], target, end);
    }
}
