namespace RaiderLite.Core.Model;

/// <summary>What a rescaled table holds where the new axis reaches beyond the old one.</summary>
public enum BeyondRange
{
    /// <summary>
    /// The edge values. ECU lookups clamp at the last breakpoint, so this is what the ECU already
    /// uses there: the engine behaves exactly as before until the new region is tuned.
    /// </summary>
    Hold,

    /// <summary>Continues the slope of the first or last segment (a starting point, not the current behaviour).</summary>
    Extrapolate,
}

/// <summary>
/// Rescales one axis of a curve (2D) or map (3D) and resamples the data along it, so that an
/// interpolated lookup gives the same result as before wherever the new breakpoints allow.
/// Everything works on raw values, as the ECU does: lookups interpolate linearly between the raw
/// breakpoints and clamp at the ends.
/// </summary>
public static class AxisRescaler
{
    /// <summary>Why <paramref name="axis"/> of <paramref name="table"/> can't be rescaled, or null if it can.</summary>
    public static string? CannotRescale(Table table, Table1D axis)
    {
        if (table is not (Table2D or Table3D))
            return "Only tables with an axis (curves and maps) can be rescaled.";
        if (!table.Axes.Contains(axis))
            return "The axis doesn't belong to this table.";
        if (axis.IsStaticDataTable)
            return "The axis is a set of labels, not values stored in the image.";
        int n = axis.Cells.Length;
        if (n < 2)
            return "The axis needs at least two breakpoints.";
        var lines = Lines(table, axis);
        if (lines.Count == 0 || lines.Any(l => l.Length != n))
            return $"The axis has {n} values but the table's rows along it don't.";
        if (table.ReadOnlyReason is { } tableReason)
            return $"The table is read-only ({tableReason}).";
        if (axis.ReadOnlyReason is { } axisReason)
            return $"The axis is read-only ({axisReason}).";
        var values = axis.Cells.Select(c => c.BinValue).ToArray();
        if (Direction(values) == 0)
        {
            return values.All(v => v == values[0])
                ? "All the axis values are the same, so it has no range to rescale."
                : "The axis goes both up and down, so lookups along it aren't defined.";
        }
        if (Overlaps(axis.Cells, table.DataCells))
            return "The axis shares bytes with the table's own data (a definition error).";
        return null;
    }

    /// <summary>
    /// The rows of data cells that run along <paramref name="axis"/>, each ordered like the axis:
    /// the single row of a curve; for a map, one row per Y breakpoint (X axis) or one column per X
    /// breakpoint (Y axis).
    /// </summary>
    public static IReadOnlyList<DataCell[]> Lines(Table table, Table1D axis)
    {
        switch (table)
        {
            case Table2D:
                return [table.Cells];
            case Table3D t3:
                var data = t3.Data3D;
                int sizeX = data.GetLength(0), sizeY = data.GetLength(1);
                if (ReferenceEquals(axis, t3.XAxis))
                    return Enumerable.Range(0, sizeY).Select(y => Enumerable.Range(0, sizeX).Select(x => data[x, y]).ToArray()).ToList();
                return Enumerable.Range(0, sizeX).Select(x => Enumerable.Range(0, sizeY).Select(y => data[x, y]).ToArray()).ToList();
            default:
                return [];
        }
    }

    /// <summary>
    /// +1 if the axis never decreases (and rises somewhere), -1 if it never increases (and falls
    /// somewhere), 0 if it is flat or goes both ways. Repeated breakpoints are allowed: real
    /// calibrations use them to pad unused breakpoints or to make a step.
    /// </summary>
    public static int Direction(IReadOnlyList<double> axis)
    {
        int direction = 0;
        for (int i = 1; i < axis.Count; i++)
        {
            int step = Math.Sign(axis[i] - axis[i - 1]);
            if (step == 0)
                continue;
            if (direction != 0 && step != direction)
                return 0;
            direction = step;
        }
        return direction;
    }

    /// <summary>
    /// The first breakpoint (1-based, the later of the pair) where <paramref name="newAxis"/> doesn't
    /// keep <paramref name="oldAxis"/>'s direction, or -1. Where the old axis repeats a value the new
    /// one may repeat it too or move on; elsewhere it must keep going the same way.
    /// </summary>
    public static int FirstBadStep(IReadOnlyList<double> oldAxis, IReadOnlyList<double> newAxis)
    {
        int direction = Direction(oldAxis);
        for (int i = 1; i < newAxis.Count; i++)
        {
            int step = Math.Sign(newAxis[i] - newAxis[i - 1]);
            int oldStep = Math.Sign(oldAxis[i] - oldAxis[i - 1]);
            if (step == -direction || (step == 0 && oldStep != 0))
                return i + 1;
        }
        return -1;
    }

    /// <summary>
    /// An ECU-style lookup: linear interpolation between the breakpoints, clamped to the edge values
    /// outside them. <paramref name="axis"/> may increase or decrease. Where a breakpoint repeats, the
    /// lookup jumps there: from that point on it uses the last of the repeated breakpoints (the usual
    /// ECU search moves past equal breakpoints); <see cref="LookupFromBefore"/> gives the value just before.
    /// </summary>
    public static double Lookup(IReadOnlyList<double> axis, IReadOnlyList<double> values, double x)
    {
        int n = axis.Count;
        if (n == 1)
            return values[0];
        double dir = axis[n - 1] >= axis[0] ? 1 : -1;
        double t = x * dir;
        if (t < axis[0] * dir)
            return values[0];
        // The last breakpoint at or before x.
        int i = n - 1;
        while (i > 0 && axis[i] * dir > t)
            i--;
        // Exactly on a breakpoint: its own value, so a lookup there (and a rescale onto the same
        // axis) is exact rather than off by rounding in the interpolation.
        if (i == n - 1 || x == axis[i])
            return values[i];
        return Table.LinearInterpolation(x, axis[i], axis[i + 1], values[i], values[i + 1]);
    }

    /// <summary>
    /// The value the lookup approaches just before <paramref name="x"/> (coming from the axis's first
    /// end). The same as <see cref="Lookup"/> except at a repeated breakpoint, where the lookup jumps.
    /// </summary>
    public static double LookupFromBefore(IReadOnlyList<double> axis, IReadOnlyList<double> values, double x)
    {
        int n = axis.Count;
        if (n == 1)
            return values[0];
        double dir = axis[n - 1] >= axis[0] ? 1 : -1;
        double t = x * dir;
        if (t <= axis[0] * dir)
            return values[0];
        // The first breakpoint at or after x.
        int i = 0;
        while (i < n && axis[i] * dir < t)
            i++;
        if (i == n)
            return values[n - 1];
        if (x == axis[i])
            return values[i];
        return Table.LinearInterpolation(x, axis[i - 1], axis[i], values[i - 1], values[i]);
    }

    /// <summary>
    /// The values at <paramref name="newAxis"/> that reproduce the table's current lookups. Within
    /// the old axis range each value is the old lookup at its breakpoint; beyond it, see
    /// <paramref name="beyond"/>. Where the new axis repeats a breakpoint, the first of the repeats
    /// takes the value from before it and the last the value from it on, so a step is kept; the
    /// ones between are never used by a lookup and keep their old values when the old axis repeats
    /// there too.
    /// </summary>
    public static double[] Resample(IReadOnlyList<double> oldAxis, IReadOnlyList<double> values, IReadOnlyList<double> newAxis, BeyondRange beyond)
    {
        int n = oldAxis.Count, m = newAxis.Count;
        double lo = Math.Min(oldAxis[0], oldAxis[n - 1]);
        double hi = Math.Max(oldAxis[0], oldAxis[n - 1]);
        var result = new double[m];
        for (int first = 0; first < m;)
        {
            int last = first;
            while (last + 1 < m && newAxis[last + 1] == newAxis[first])
                last++;
            double x = newAxis[first];
            bool extrapolate = beyond == BeyondRange.Extrapolate && (x < lo || x > hi);
            bool oldRepeatsHere = last < n && Enumerable.Range(first, last - first + 1).All(i => oldAxis[i] == oldAxis[first]);
            for (int i = first; i <= last; i++)
            {
                if (extrapolate)
                    result[i] = Extrapolate(oldAxis, values, x);
                else if (i == first && last > first)
                    result[i] = LookupFromBefore(oldAxis, values, x);
                else if (i == last || !oldRepeatsHere)
                    result[i] = Lookup(oldAxis, values, x);
                else
                    result[i] = values[i];
            }
            first = last + 1;
        }
        return result;
    }

    /// <summary>Continues the first or last segment (that isn't a repeated breakpoint) past the axis's end.</summary>
    private static double Extrapolate(IReadOnlyList<double> axis, IReadOnlyList<double> values, double x)
    {
        int n = axis.Count;
        double dir = axis[n - 1] >= axis[0] ? 1 : -1;
        bool beforeFirst = (x - axis[0]) * dir < 0;
        int j = beforeFirst ? 0 : n - 2;
        while (j >= 0 && j < n - 1 && axis[j] == axis[j + 1])
            j += beforeFirst ? 1 : -1;
        if (j < 0 || j >= n - 1)
            return Lookup(axis, values, x);
        return Table.LinearInterpolation(x, axis[j], axis[j + 1], values[j], values[j + 1]);
    }

    /// <summary>
    /// The largest difference between two lookups over [<paramref name="lo"/>, <paramref name="hi"/>] of
    /// the axis, and where it occurs. Both lookups are piecewise linear, with corners and jumps only
    /// at their breakpoints, so their difference is too: checking the breakpoints (and the interval
    /// ends) from both sides finds the exact maximum. <paramref name="toReal"/> converts values
    /// before comparing. Both axes must run the same way.
    /// </summary>
    public static (double Difference, double At) MaxDifference(
        IReadOnlyList<double> axisA, IReadOnlyList<double> valuesA,
        IReadOnlyList<double> axisB, IReadOnlyList<double> valuesB,
        double lo, double hi, Func<double, double>? toReal = null)
    {
        toReal ??= v => v;
        // The interval's first end, seen from the axis's first end: the value just before it lies outside.
        double start = axisA[^1] >= axisA[0] ? lo : hi;
        double best = 0, at = lo;
        foreach (double x in axisA.Concat(axisB).Append(lo).Append(hi))
        {
            if (x < lo || x > hi)
                continue;
            double difference = toReal(Lookup(axisB, valuesB, x)) - toReal(Lookup(axisA, valuesA, x));
            if (x != start)
            {
                double before = toReal(LookupFromBefore(axisB, valuesB, x)) - toReal(LookupFromBefore(axisA, valuesA, x));
                if (Math.Abs(before) > Math.Abs(difference))
                    difference = before;
            }
            if (Math.Abs(difference) > Math.Abs(best))
            {
                best = difference;
                at = x;
            }
        }
        return (best, at);
    }

    /// <summary>
    /// Keeps the first breakpoint and the relative spacing, moving the last breakpoint to
    /// <paramref name="newLast"/>: dense regions stay dense.
    /// </summary>
    public static double[] Stretch(IReadOnlyList<double> axis, double newLast)
    {
        int n = axis.Count;
        double first = axis[0], last = axis[n - 1];
        if (last == first)
            return Even(first, newLast, n);
        var result = new double[n];
        for (int i = 0; i < n; i++)
            result[i] = first + (axis[i] - first) * (newLast - first) / (last - first);
        result[n - 1] = newLast;
        return result;
    }

    /// <summary><paramref name="count"/> evenly spaced breakpoints from <paramref name="first"/> to <paramref name="last"/>.</summary>
    public static double[] Even(double first, double last, int count)
    {
        var result = new double[count];
        for (int i = 0; i < count; i++)
            result[i] = count == 1 ? first : first + (last - first) * i / (count - 1);
        if (count > 1)
            result[count - 1] = last;
        return result;
    }

    /// <summary>
    /// Works out the rescale of <paramref name="axis"/> to the real-unit breakpoints
    /// <paramref name="newAxisReal"/> without changing anything; see <see cref="AxisRescalePlan.Apply"/>.
    /// </summary>
    public static AxisRescalePlan Plan(Table table, Table1D axis, IReadOnlyList<double> newAxisReal, BeyondRange beyond) =>
        new(table, axis, newAxisReal, beyond);

    private static bool Overlaps(IEnumerable<DataCell> a, IEnumerable<DataCell> b)
    {
        var bytes = new HashSet<int>();
        foreach (var cell in a.Where(c => !c.IsStatic))
        {
            for (int i = 0; i < StorageType.ByteLength(cell.Table.StorageType); i++)
                bytes.Add(cell.Address + i);
        }
        return b.Where(c => !c.IsStatic).Any(cell =>
            Enumerable.Range(cell.Address, StorageType.ByteLength(cell.Table.StorageType)).Any(bytes.Contains));
    }
}

/// <summary>
/// A worked-out axis rescale: the new axis and data exactly as they will be stored, and how much
/// the table's lookups change. Nothing is written until <see cref="Apply"/>.
/// </summary>
public sealed class AxisRescalePlan
{
    private readonly List<string> _warnings = [];

    internal AxisRescalePlan(Table table, Table1D axis, IReadOnlyList<double> newAxisReal, BeyondRange beyond)
    {
        Table = table;
        Axis = axis;
        Beyond = beyond;
        OldAxis = axis.Cells.Select(c => c.IsStatic ? double.NaN : c.BinValue).ToArray();
        Error = AxisRescaler.CannotRescale(table, axis) ?? Build(newAxisReal);
    }

    public Table Table { get; }

    public Table1D Axis { get; }

    public BeyondRange Beyond { get; }

    /// <summary>Raw axis values now.</summary>
    public double[] OldAxis { get; }

    /// <summary>Raw axis values as they will be stored (empty if there is an <see cref="Error"/>).</summary>
    public double[] NewAxis { get; private set; } = [];

    /// <summary>The data cells along the axis (see <see cref="AxisRescaler.Lines"/>).</summary>
    public IReadOnlyList<DataCell[]> Lines { get; private set; } = [];

    /// <summary>Raw values of each line now.</summary>
    public double[][] OldValues { get; private set; } = [];

    /// <summary>Raw values of each line as they will be stored.</summary>
    public double[][] NewValues { get; private set; } = [];

    /// <summary>Why the rescale can't be done, or null.</summary>
    public string? Error { get; }

    public bool CanApply => Error == null;

    /// <summary>Things to know before applying (other tables affected, values limited...).</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Largest change, in the table's current units, of any lookup within the old axis range
    /// (signed: new minus old).
    /// </summary>
    public double MaxChange { get; private set; }

    /// <summary>Where (axis value, in the axis's current units) <see cref="MaxChange"/> occurs.</summary>
    public double MaxChangeAt { get; private set; }

    /// <summary>Range of the table's current values, in its units (for judging <see cref="MaxChange"/>).</summary>
    public double DataRange { get; private set; }

    /// <summary>New values that had to be limited to what the data cells can store (extrapolation).</summary>
    public int LimitedValues { get; private set; }

    private string? Build(IReadOnlyList<double> newAxisReal)
    {
        var cells = Axis.Cells;
        int n = cells.Length;
        if (newAxisReal.Count != n)
            return $"The axis has {n} breakpoints; {newAxisReal.Count} values were given.";

        var scale = Axis.CurrentScale;
        var newAxis = new double[n];
        for (int i = 0; i < n; i++)
        {
            double real = newAxisReal[i];
            if (!double.IsFinite(real))
                return $"Breakpoint {i + 1} is not a number.";
            double bin = scale.ToBin(real, Axis.StorageType, Axis.Signed);
            if (!double.IsFinite(bin))
                return $"{Show(real, scale)} can't be converted to the axis's stored form.";
            if (Axis.StorageType != StorageType.Float)
                bin = Math.Round(bin, MidpointRounding.AwayFromZero);
            var cell = cells[i];
            if (bin < cell.MinAllowedBin || bin > cell.MaxAllowedBin)
            {
                double a = scale.ToReal(cell.MinAllowedBin), b = scale.ToReal(cell.MaxAllowedBin);
                return $"{Show(real, scale)} is outside what the axis can store ({Show(Math.Min(a, b), scale)} to {Show(Math.Max(a, b), scale)}).";
            }
            newAxis[i] = cell.ClosestStorable(bin);
        }

        int oldDirection = AxisRescaler.Direction(OldAxis);
        string way = oldDirection > 0 ? "increasing" : "decreasing";
        if (AxisRescaler.Direction(newAxis) == -oldDirection)
            return $"The new axis runs in the opposite direction to the current one (it must keep {way}).";
        int bad = AxisRescaler.FirstBadStep(OldAxis, newAxis);
        if (bad > 0)
        {
            string a = Show(scale.ToReal(newAxis[bad - 2]), scale), b = Show(scale.ToReal(newAxis[bad - 1]), scale);
            return newAxis[bad - 2] == newAxis[bad - 1]
                ? $"Breakpoints {bad - 1} and {bad} would both be stored as {a}: the axis's resolution can't separate them."
                : $"Breakpoints {bad - 1} and {bad} would be stored as {a} and {b}: the axis must keep {way}.";
        }
        NewAxis = newAxis;

        Lines = AxisRescaler.Lines(Table, Axis);
        OldValues = Lines.Select(line => line.Select(c => c.BinValue).ToArray()).ToArray();
        NewValues = new double[Lines.Count][];
        for (int j = 0; j < Lines.Count; j++)
        {
            var resampled = AxisRescaler.Resample(OldAxis, OldValues[j], newAxis, Beyond);
            for (int i = 0; i < n; i++)
            {
                var cell = Lines[j][i];
                if (resampled[i] < cell.MinAllowedBin || resampled[i] > cell.MaxAllowedBin)
                    LimitedValues++;
                resampled[i] = cell.ClosestStorable(resampled[i]);
            }
            NewValues[j] = resampled;
        }

        Measure();
        AddWarnings();
        return null;
    }

    /// <summary>Largest change of any lookup within the old axis range, over all lines.</summary>
    private void Measure()
    {
        var dataScale = Table.CurrentScale;
        double lo = Math.Min(OldAxis[0], OldAxis[^1]);
        double hi = Math.Max(OldAxis[0], OldAxis[^1]);
        double best = 0, at = OldAxis[0];
        for (int j = 0; j < Lines.Count; j++)
        {
            var (difference, x) = AxisRescaler.MaxDifference(OldAxis, OldValues[j], NewAxis, NewValues[j], lo, hi, dataScale.ToReal);
            if (Math.Abs(difference) > Math.Abs(best))
            {
                best = difference;
                at = x;
            }
        }
        MaxChange = best;
        MaxChangeAt = Axis.CurrentScale.ToReal(at);
        var reals = OldValues.SelectMany(v => v).Select(dataScale.ToReal).ToList();
        DataRange = reals.Count > 0 ? reals.Max() - reals.Min() : 0;
    }

    private void AddWarnings()
    {
        double oldLo = Math.Min(OldAxis[0], OldAxis[^1]), oldHi = Math.Max(OldAxis[0], OldAxis[^1]);
        double newLo = Math.Min(NewAxis[0], NewAxis[^1]), newHi = Math.Max(NewAxis[0], NewAxis[^1]);
        if (newLo > oldLo || newHi < oldHi)
        {
            _warnings.Add("The new axis doesn't cover the whole current range: beyond its ends the ECU will use the edge values, " +
                          "so its behaviour changes there.");
        }
        if (LimitedValues > 0)
            _warnings.Add($"{LimitedValues} extrapolated value(s) were limited to what the table can store.");
        if (Table.Rom is { } rom)
        {
            var shared = rom.TablesSharingBytes(Axis.Cells.Concat(Table.DataCells));
            if (shared.Count > 0)
            {
                string names = string.Join(", ", shared.Take(5).Select(t => t.Name)) + (shared.Count > 5 ? $" and {shared.Count - 5} more" : "");
                _warnings.Add($"Also changes {names}: {(shared.Count == 1 ? "it uses" : "they use")} some of the same bytes, " +
                              "but nothing else in those tables is resampled.");
            }
        }
    }

    private static string Show(double real, Scale scale) =>
        (ValueFormatter.Format(real, scale.Format) + (scale.IsRaw ? "" : " " + scale.Unit)).Trim();

    /// <summary>
    /// Writes the new axis and data as one undo step. If the axis can't be stored exactly as planned
    /// (an unusual encoding), the data is resampled against what was actually stored.
    /// </summary>
    /// <exception cref="InvalidOperationException">The plan has an error, or the table changed since it was made.</exception>
    /// <exception cref="UserLevelException">The table can't be edited at the current user level.</exception>
    public void Apply()
    {
        if (Error != null)
            throw new InvalidOperationException(Error);
        var axisCells = Axis.Cells;
        bool unchanged = axisCells.Select(c => c.BinValue).SequenceEqual(OldAxis) &&
                         Lines.Select(l => l.Select(c => c.BinValue)).Zip(OldValues).All(p => p.First.SequenceEqual(p.Second));
        if (!unchanged)
            throw new InvalidOperationException("The table changed after the rescale was worked out; work it out again.");

        using (Table.Rom?.History.Begin("Rescale axis"))
        {
            for (int i = 0; i < axisCells.Length; i++)
                axisCells[i].SetBinValue(NewAxis[i]);

            var stored = axisCells.Select(c => c.BinValue).ToArray();
            var values = NewValues;
            if (!stored.SequenceEqual(NewAxis))
            {
                if (AxisRescaler.FirstBadStep(OldAxis, stored) > 0)
                {
                    for (int i = 0; i < axisCells.Length; i++)
                        axisCells[i].SetBinValue(OldAxis[i]);
                    throw new InvalidOperationException("The axis couldn't be stored as planned; nothing was changed.");
                }
                values = OldValues.Select(old => AxisRescaler.Resample(OldAxis, old, stored, Beyond)).ToArray();
            }

            for (int j = 0; j < Lines.Count; j++)
            {
                for (int i = 0; i < Lines[j].Length; i++)
                    Lines[j][i].SetBinValue(values[j][i]);
            }
        }
    }
}
