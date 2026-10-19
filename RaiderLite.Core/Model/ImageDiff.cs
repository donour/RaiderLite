namespace RaiderLite.Core.Model;

/// <summary>A table whose values differ between two images, with the number of cells that differ.</summary>
public sealed record TableChange(Table Table, int ChangedCells);

/// <summary>The tables that differ between two images of the same definition.</summary>
public sealed class ImageComparison
{
    internal ImageComparison(bool sameSize, IReadOnlyList<TableChange> tables, int changedBytes, int changedBytesOutsideTables)
    {
        SameSize = sameSize;
        Tables = tables;
        ChangedBytes = changedBytes;
        ChangedBytesOutsideTables = changedBytesOutsideTables;
    }

    /// <summary>False when the images differ in length; nothing else is compared then.</summary>
    public bool SameSize { get; }

    /// <summary>Tables with at least one changed value, in definition order.</summary>
    public IReadOnlyList<TableChange> Tables { get; }

    public int ChangedBytes { get; }

    /// <summary>Changed bytes that no table covers: invisible in the editor.</summary>
    public int ChangedBytesOutsideTables { get; }

    public bool Identical => SameSize && ChangedBytes == 0;
}

public enum TableComparisonResult { Same, Different, DifferentSize, MissingInFirst, MissingInSecond }

/// <summary>One table of two images compared by name (either side may be missing).</summary>
public sealed record TableComparison(string Name, string Category, Table? First, Table? Second, TableComparisonResult Result, int ChangedCells);

/// <summary>Finds the tables that differ between images.</summary>
public static class ImageDiff
{
    /// <summary>
    /// Compares two images of <paramref name="template"/>'s definition without loading either:
    /// only the bytes that differ are decoded, through the template's cells. Both images must have
    /// the template's length (the RAM offset depends on it); otherwise the result has
    /// <see cref="ImageComparison.SameSize"/> false.
    /// </summary>
    public static ImageComparison Compare(Rom template, byte[] first, byte[] second)
    {
        int length = template.Binary.Length;
        if (first.Length != length || second.Length != length)
            return new ImageComparison(false, [], 0, 0);

        int changedBytes = 0, outside = 0;
        var candidates = new HashSet<DataCell>();
        var wholeTables = new HashSet<Table>();
        for (int i = 0; i < length; i++)
        {
            if (first[i] == second[i])
                continue;
            changedBytes++;
            var cells = template.CellsAt(i);
            if (!cells.Any(c => template.IsLive(c.Table.TopLevel)))
                outside++;
            foreach (var cell in cells)
            {
                // In the Bosch subtract layout every value depends on the stored values after it.
                if (cell.Table.DataLayout == DataLayout.BoschSubtract)
                    wholeTables.Add(cell.Table);
                else
                    candidates.Add(cell);
            }
        }
        foreach (var table in wholeTables)
            candidates.UnionWith(table.Cells.Where(c => !c.IsStatic));

        var counts = new Dictionary<Table, int>();
        foreach (var cell in candidates)
        {
            var top = cell.Table.TopLevel;
            // Bits outside a cell's mask can change without changing its value. Equals (not ==)
            // so a float NaN that stayed NaN isn't a change.
            if (!template.IsLive(top) || cell.ReadValue(first).Equals(cell.ReadValue(second)))
                continue;
            counts[top] = counts.GetValueOrDefault(top) + 1;
        }
        var tables = template.Tables.Where(counts.ContainsKey).Select(t => new TableChange(t, counts[t])).ToList();
        return new ImageComparison(true, tables, changedBytes, outside);
    }

    /// <summary>
    /// Compares two loaded images table by table, matching tables by name, so the images may use
    /// different definitions (RomRaider's Compare Images).
    /// </summary>
    public static IReadOnlyList<TableComparison> CompareRoms(Rom first, Rom second)
    {
        var results = new List<TableComparison>();
        foreach (var a in first.Tables)
        {
            var b = second.GetTable(a.Name);
            if (b == null)
            {
                results.Add(new TableComparison(a.Name, a.Category, a, null, TableComparisonResult.MissingInSecond, 0));
                continue;
            }
            if (!a.IsCompatibleWith(b))
            {
                results.Add(new TableComparison(a.Name, a.Category, a, b, TableComparisonResult.DifferentSize, 0));
                continue;
            }
            int changed = a.AllCells.Zip(b.AllCells).Count(p => !p.First.IsStatic && !p.First.BinValue.Equals(p.Second.BinValue));
            results.Add(new TableComparison(a.Name, a.Category, a, b, changed == 0 ? TableComparisonResult.Same : TableComparisonResult.Different, changed));
        }
        foreach (var b in second.Tables.Where(t => first.GetTable(t.Name) == null))
            results.Add(new TableComparison(b.Name, b.Category, null, b, TableComparisonResult.MissingInFirst, 0));
        return results;
    }
}
