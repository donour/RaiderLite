using RaiderLite.Core.Model;

namespace RaiderLite.Core.Plotting;

/// <summary>One axis of a graph: where each column or row sits, and how to label it.</summary>
public sealed class GraphAxis
{
    public required string Title { get; init; }

    /// <summary>Position of each entry: the real axis value, or the index for a text (static) axis.</summary>
    public required double[] Positions { get; init; }

    /// <summary>Display text of each entry.</summary>
    public required string[] Labels { get; init; }

    /// <summary>True when the axis is text labels; positions are then evenly spaced indices.</summary>
    public bool Categorical { get; init; }

    public string? Format { get; init; }

    public int Count => Positions.Length;
}

/// <summary>
/// Snapshot of a curve (one axis) or map (two axes) ready to plot, in the tables' current
/// scalings. Values are indexed [x, y]; a curve has a single row (y = 0).
/// </summary>
public sealed class GraphData
{
    public required string Title { get; init; }

    public required GraphAxis X { get; init; }

    /// <summary>The second axis of a map; null for a curve.</summary>
    public GraphAxis? Y { get; init; }

    /// <summary>Title of the value (vertical) axis, e.g. "ms" or "raw".</summary>
    public required string ValueTitle { get; init; }

    public required string ValueFormat { get; init; }

    public required double[,] Values { get; init; }

    /// <summary>Original values where any cell has changed; otherwise null.</summary>
    public double[,]? Originals { get; init; }

    /// <summary>Values of the compared table (another image), when comparing; otherwise null.</summary>
    public double[,]? Compared { get; init; }

    public required DataCell[,] Cells { get; init; }

    public bool IsMap => Y != null;

    public int SizeX => Values.GetLength(0);

    public int SizeY => Values.GetLength(1);

    /// <summary>Smallest and largest finite value across values, originals and compared values.</summary>
    public (double Min, double Max) ValueRange
    {
        get
        {
            double min = double.MaxValue, max = double.MinValue;
            foreach (var grid in new[] { Values, Originals, Compared })
            {
                if (grid == null)
                    continue;
                foreach (var v in grid)
                {
                    if (!double.IsFinite(v))
                        continue;
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                }
            }
            return min <= max ? (min, max) : (0, 1);
        }
    }

    /// <summary>Whether a table can be graphed: curves (arrays) and maps; not scalars or switches.</summary>
    public static bool CanGraph(Table table) =>
        table.Shape is TableShape.Array or TableShape.Map && table is not TableSwitch;

    /// <summary>Builds the graph data of a curve or map, or null if the table can't be graphed.</summary>
    public static GraphData? From(Table table)
    {
        if (!CanGraph(table))
            return null;
        return table switch
        {
            Table3D t3 => FromMap(t3),
            Table2D t2 => FromCells(table.Name, AxisFrom(t2.Axis, t2.Cells.Length), t2.Cells, table),
            _ => FromCells(table.Name, IndexAxis(table.Cells.Length), table.Cells, table),
        };
    }

    private static GraphData FromCells(string title, GraphAxis x, DataCell[] cells, Table table)
    {
        int n = cells.Length;
        var grid = new DataCell[n, 1];
        for (int i = 0; i < n; i++)
            grid[i, 0] = cells[i];
        return Build(title, x, null, grid, table);
    }

    private static GraphData FromMap(Table3D table)
    {
        var data = table.Data3D;
        int sx = data.GetLength(0), sy = data.GetLength(1);
        return Build(table.Name, AxisFrom(table.XAxis, sx), AxisFrom(table.YAxis, sy), (DataCell[,])data.Clone(), table);
    }

    private static GraphData Build(string title, GraphAxis x, GraphAxis? y, DataCell[,] cells, Table table)
    {
        int sx = cells.GetLength(0), sy = cells.GetLength(1);
        var scale = table.CurrentScale;
        var values = new double[sx, sy];
        var originals = new double[sx, sy];
        bool anyChanged = false;
        bool comparing = table.CompareTable != null && !ReferenceEquals(table.CompareTable, table);
        var compared = comparing ? new double[sx, sy] : null;

        for (int i = 0; i < sx; i++)
        {
            for (int j = 0; j < sy; j++)
            {
                var cell = cells[i, j];
                values[i, j] = cell.RealValue;
                originals[i, j] = scale.ToReal(cell.OriginalValue);
                anyChanged |= cell.IsChanged;
                if (compared != null)
                    compared[i, j] = scale.ToReal(cell.CompareToValue);
            }
        }

        return new GraphData
        {
            Title = title,
            X = x,
            Y = y,
            ValueTitle = scale.IsRaw ? "raw" : scale.Unit,
            ValueFormat = scale.Format,
            Values = values,
            Originals = anyChanged ? originals : null,
            Compared = compared,
            Cells = cells,
        };
    }

    private static GraphAxis AxisFrom(Table1D axis, int count)
    {
        var scale = axis.CurrentScale;
        var positions = new double[count];
        var labels = new string[count];
        bool categorical = axis.IsStaticDataTable || axis.Cells.Length < count;
        for (int i = 0; i < count; i++)
        {
            var cell = i < axis.Cells.Length ? axis.Cells[i] : null;
            if (categorical || cell == null)
            {
                positions[i] = i;
                labels[i] = cell?.StaticText ?? (i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);
            }
            else
            {
                positions[i] = cell.RealValue;
                labels[i] = ValueFormatter.Format(cell.RealValue, scale.Format);
            }
        }

        // An axis whose values aren't usable as positions (NaN, all equal) is plotted by index.
        if (!categorical && (positions.Any(p => !double.IsFinite(p)) || positions.Distinct().Count() < Math.Min(2, count)))
        {
            for (int i = 0; i < count; i++)
                positions[i] = i;
            categorical = true;
        }

        string units = scale.IsRaw || axis.IsStaticDataTable ? "" : scale.Unit;
        string title = string.IsNullOrEmpty(axis.Name) ? units : units.Length > 0 ? $"{axis.Name} ({units})" : axis.Name;
        return new GraphAxis { Title = title, Positions = positions, Labels = labels, Categorical = categorical, Format = scale.Format };
    }

    private static GraphAxis IndexAxis(int count) => new()
    {
        Title = "index",
        Positions = Enumerable.Range(0, count).Select(i => (double)i).ToArray(),
        Labels = Enumerable.Range(0, count).Select(i => i.ToString(System.Globalization.CultureInfo.CurrentCulture)).ToArray(),
        Categorical = true,
    };

    /// <summary>The same map with X and Y swapped (one line per column instead of per row).</summary>
    public GraphData Transposed()
    {
        if (Y == null)
            return this;
        static double[,] Swap(double[,] a)
        {
            var r = new double[a.GetLength(1), a.GetLength(0)];
            for (int i = 0; i < a.GetLength(0); i++)
                for (int j = 0; j < a.GetLength(1); j++)
                    r[j, i] = a[i, j];
            return r;
        }
        var cells = new DataCell[SizeY, SizeX];
        for (int i = 0; i < SizeX; i++)
            for (int j = 0; j < SizeY; j++)
                cells[j, i] = Cells[i, j];
        return new GraphData
        {
            Title = Title,
            X = Y,
            Y = X,
            ValueTitle = ValueTitle,
            ValueFormat = ValueFormat,
            Values = Swap(Values),
            Originals = Originals != null ? Swap(Originals) : null,
            Compared = Compared != null ? Swap(Compared) : null,
            Cells = cells,
        };
    }
}
