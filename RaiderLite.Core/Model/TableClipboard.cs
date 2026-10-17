using System.Text;

namespace RaiderLite.Core.Model;

/// <summary>
/// RomRaider-compatible clipboard text: "[Table1D]", "[Table2D]", "[Table3D]",
/// "[Selection1D]" and "[Selection3D]" headers followed by tab/newline separated
/// real values ("x" marks an unselected cell). Text without a header (e.g. cells
/// copied from a spreadsheet) is pasted as a block at the selection.
/// </summary>
public static class TableClipboard
{
    public const string Table1DHeader = "[Table1D]";
    public const string Table2DHeader = "[Table2D]";
    public const string Table3DHeader = "[Table3D]";
    public const string Selection1DHeader = "[Selection1D]";
    public const string Selection3DHeader = "[Selection3D]";

    private static readonly string NewLine = Environment.NewLine;

    // ----- copy -----

    /// <summary>Copies the selected cells, or returns null if nothing is selected.</summary>
    public static string? CopySelection(Table table)
    {
        switch (table)
        {
            case Table3D t3:
                return CopySelection3D(t3) ?? CopySelection1D(t3.XAxis.Cells) ?? CopySelection1D(t3.YAxis.Cells);
            case Table2D t2:
                return CopySelection1D(t2.Cells) ?? CopySelection1D(t2.Axis.Cells);
            default:
                return CopySelection1D(table.Cells);
        }
    }

    private static string? CopySelection1D(DataCell[] cells)
    {
        int first = cells.Length, last = -1;
        for (int i = 0; i < cells.Length; i++)
        {
            if (!cells[i].IsSelected)
                continue;
            first = Math.Min(first, i);
            last = Math.Max(last, i);
        }
        if (last < 0)
            return null;

        var sb = new StringBuilder(Selection1DHeader).Append(NewLine);
        for (int i = first; i <= last; i++)
        {
            sb.Append(cells[i].IsSelected ? ValueFormatter.ToClipboardString(cells[i].RealValue) : "x");
            if (i < last)
                sb.Append('\t');
        }
        return sb.ToString();
    }

    private static string? CopySelection3D(Table3D table)
    {
        var data = table.Data3D;
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = -1, y2 = -1;
        for (int x = 0; x < data.GetLength(0); x++)
        {
            for (int y = 0; y < data.GetLength(1); y++)
            {
                if (!data[x, y].IsSelected)
                    continue;
                x1 = Math.Min(x1, x);
                x2 = Math.Max(x2, x);
                y1 = Math.Min(y1, y);
                y2 = Math.Max(y2, y);
            }
        }
        if (x2 < 0)
            return null;

        var sb = new StringBuilder(Selection3DHeader).Append(NewLine);
        for (int y = y1; y <= y2; y++)
        {
            for (int x = x1; x <= x2; x++)
            {
                sb.Append(data[x, y].IsSelected ? ValueFormatter.ToClipboardString(data[x, y].RealValue) : "x");
                if (x < x2)
                    sb.Append('\t');
            }
            if (y < y2)
                sb.Append(NewLine);
        }
        return sb.ToString();
    }

    /// <summary>Copies the whole table, with axes.</summary>
    public static string CopyTable(Table table)
    {
        string header = table switch
        {
            Table3D => Table3DHeader,
            Table2D => Table2DHeader,
            _ => Table1DHeader,
        };
        return header + NewLine + table.GetTableAsString();
    }

    // ----- paste -----

    private static List<string[]> SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Split(['\t', '\f'], StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToArray())
            .ToList();

    /// <summary>Pastes clipboard text into the table. Edits are grouped as one undo step.</summary>
    public static void Paste(Table table, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var lines = SplitLines(text.TrimEnd());
        // Drop leading blank lines.
        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);
        if (lines.Count == 0)
            return;

        string first = lines[0][0];
        bool hasHeader = first.StartsWith('[') && first.EndsWith(']');
        string header = hasHeader ? first : "";
        if (hasHeader)
        {
            // The header may share its line with data (e.g. "[Table3D]\t..." in the Airboys format).
            lines[0] = lines[0][1..];
            if (lines[0].Length == 0)
                lines.RemoveAt(0);
        }
        lines.RemoveAll(l => l.Length == 0);
        if (lines.Count == 0)
            return;

        IDisposable? group = table.Rom?.History.Begin("Paste");
        try
        {
            switch (table)
            {
                case Table3D t3:
                    Paste3D(t3, header, lines);
                    break;
                case Table2D t2:
                    Paste2D(t2, header, lines);
                    break;
                default:
                    // Whole 2D/3D tables don't fit a 1D table.
                    if (header != Table2DHeader && header != Table3DHeader)
                        Paste1D(table.Cells, header, lines.SelectMany(l => l).ToArray(), requireSelection: header == Selection1DHeader);
                    break;
            }
        }
        finally
        {
            group?.Dispose();
        }
    }

    private static int FirstSelected(DataCell[] cells)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i].IsSelected)
                return i;
        }
        return -1;
    }

    private static void SetIfDifferent(DataCell cell, string token)
    {
        if (cell.IsStatic || token.Equals("x", StringComparison.OrdinalIgnoreCase))
            return;
        // Leave cells alone whose displayed value already matches (avoids rounding drift).
        var format = cell.Table.CurrentScale.Format;
        if (ValueFormatter.Format(cell.RealValue, format) == token)
            return;
        cell.SetRealValue(token);
    }

    private static void Paste1D(DataCell[] cells, string header, string[] tokens, bool requireSelection)
    {
        if (cells.Length == 0 || cells[0].IsStatic)
            return;
        int start = FirstSelected(cells);
        if (start < 0)
        {
            if (requireSelection)
                return;
            start = 0;
        }

        for (int i = 0; i < tokens.Length && start + i < cells.Length; i++)
            SetIfDifferent(cells[start + i], tokens[i]);
    }

    private static void Paste2D(Table2D table, string header, List<string[]> lines)
    {
        if (header == Table2DHeader)
        {
            if (lines.Count >= 2)
            {
                Paste1D(table.Axis.Cells, Table1DHeader, lines[0], requireSelection: false);
                PasteFromStart(table.Cells, lines[1]);
            }
            else
            {
                PasteFromStart(table.Cells, lines[0]);
            }
            return;
        }

        var tokens = lines.SelectMany(l => l).ToArray();
        bool dataSelected = FirstSelected(table.Cells) >= 0;
        bool axisSelected = FirstSelected(table.Axis.Cells) >= 0;
        if (header == Selection1DHeader || header.Length == 0)
        {
            if (dataSelected || !axisSelected)
                Paste1D(table.Cells, header, tokens, requireSelection: header == Selection1DHeader);
            else
                Paste1D(table.Axis.Cells, header, tokens, requireSelection: true);
        }
        else if (header == Table1DHeader)
        {
            PasteFromStart(table.Cells, tokens);
        }
    }

    private static void PasteFromStart(DataCell[] cells, string[] tokens)
    {
        for (int i = 0; i < tokens.Length && i < cells.Length; i++)
            SetIfDifferent(cells[i], tokens[i]);
    }

    private static void Paste3D(Table3D table, string header, List<string[]> lines)
    {
        var data = table.Data3D;
        int sizeX = data.GetLength(0);
        int sizeY = data.GetLength(1);

        if (header == Table3DHeader)
        {
            // First line: X axis. Then each line: Y axis value followed by a row of data.
            Paste1D(table.XAxis.Cells, Table1DHeader, lines[0], requireSelection: false);
            var yValues = new List<string>();
            for (int row = 1; row < lines.Count; row++)
            {
                if (lines[row].Length == 0)
                    continue;
                yValues.Add(lines[row][0]);
                int y = row - 1;
                if (y >= sizeY)
                    continue;
                for (int x = 0; x + 1 < lines[row].Length && x < sizeX; x++)
                    SetIfDifferent(data[x, y], lines[row][x + 1]);
            }
            Paste1D(table.YAxis.Cells, Table1DHeader, [.. yValues], requireSelection: false);
            return;
        }

        bool dataSelected = table.DataCells.Any(c => c.IsSelected);
        if (header == Selection1DHeader || (header.Length == 0 && !dataSelected && lines.Count == 1))
        {
            var tokens = lines.SelectMany(l => l).ToArray();
            if (!dataSelected)
            {
                if (FirstSelected(table.XAxis.Cells) >= 0)
                    Paste1D(table.XAxis.Cells, Selection1DHeader, tokens, requireSelection: true);
                if (FirstSelected(table.YAxis.Cells) >= 0)
                    Paste1D(table.YAxis.Cells, Selection1DHeader, tokens, requireSelection: true);
                return;
            }
        }

        if (header == Selection3DHeader || header == Selection1DHeader || header.Length == 0)
        {
            // Paste a block with its top-left at the top-left of the selection.
            int startX = int.MaxValue, startY = int.MaxValue;
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    if (!data[x, y].IsSelected)
                        continue;
                    startX = Math.Min(startX, x);
                    startY = Math.Min(startY, y);
                }
            }
            if (startX == int.MaxValue)
            {
                if (header.Length != 0)
                    return;
                startX = startY = 0;
            }

            for (int row = 0; row < lines.Count && startY + row < sizeY; row++)
            {
                for (int col = 0; col < lines[row].Length && startX + col < sizeX; col++)
                    SetIfDifferent(data[startX + col, startY + row], lines[row][col]);
            }
        }
    }
}
