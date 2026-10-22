using System.Text;

namespace RaiderLite.Core.Model;

/// <summary>A 1D table: a scalar or a plain array. Also used for axes.</summary>
public class Table1D : Table
{
    public override TableType Type => TableType.Table1D;

    /// <summary>The 2D/3D table this axis belongs to; null for stand-alone tables.</summary>
    public Table? AxisParent { get; internal set; }

    /// <summary>For axes: whether this is the X or Y axis.</summary>
    public AxisKind AxisKind { get; set; }

    public bool IsAxis => AxisParent != null;

    public override Table TopLevel => AxisParent ?? this;
}

/// <summary>A 2D table: one axis plus a row of data (a curve).</summary>
public class Table2D : Table
{
    private Table1D _axis;

    public Table2D()
    {
        _axis = new Table1D { AxisKind = AxisKind.X };
        _axis.AxisParent = this;
    }

    public override TableType Type => TableType.Table2D;

    public Table1D Axis
    {
        get => _axis;
        set
        {
            _axis = value;
            value.AxisParent = this;
        }
    }

    public override IEnumerable<Table1D> Axes => [_axis];

    public override Table Clone()
    {
        var copy = (Table2D)base.Clone();
        copy.Axis = (Table1D)_axis.Clone();
        return copy;
    }

    public override void Populate(Rom rom, IList<string> warnings)
    {
        _axis.Populate(rom, warnings);
        base.Populate(rom, warnings);
        SyncAxisScale(_axis, CurrentScale);
    }

    public override void SetCurrentScale(Scale scale)
    {
        SyncAxisScale(_axis, scale);
        base.SetCurrentScale(scale);
    }

    /// <summary>Interpolates data along the axis values (by raw value, as RomRaider does).</summary>
    public override void HorizontalInterpolate()
    {
        var data = Cells;
        var axis = _axis.Cells;
        int first = data.Length, last = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (!data[i].IsSelected)
                continue;
            first = Math.Min(first, i);
            last = Math.Max(last, i);
        }
        if (last - first > 1 && axis.Length == data.Length)
        {
            double x1 = AxisPosition(axis, first);
            double x2 = AxisPosition(axis, last);
            double y1 = data[first].BinValue;
            double y2 = data[last].BinValue;
            for (int i = first + 1; i < last; i++)
                SetInterpolated(data[i], LinearInterpolation(AxisPosition(axis, i), x1, x2, y1, y2));
        }
        else if (last - first > 1)
        {
            InterpolateByIndex(data);
        }
        _axis.HorizontalInterpolate();
    }

    /// <summary>Static axes have no numeric values, so interpolate by index there.</summary>
    internal static double AxisPosition(DataCell[] axis, int i) => axis[i].IsStatic ? i : axis[i].BinValue;

    public override bool IsCompatibleWith(Table other) =>
        other is Table2D o && o.Cells.Length == Cells.Length && o._axis.Cells.Length == _axis.Cells.Length;

    protected override void PopulateCompareValues(Table other)
    {
        if (!IsCompatibleWith(other))
            return;
        var o = (Table2D)other;
        if (InterpolateCompare && _axis.Cells.Length == Cells.Length)
        {
            var (mine, theirs) = LookupAxes(_axis, o._axis, CompareValueType);
            for (int i = 0; i < Cells.Length; i++)
            {
                var (lo, hi, t) = Bracket(theirs, mine[i]);
                Cells[i].CompareToValue = Lerp(CompareSource(o.Cells[lo], CompareValueType), CompareSource(o.Cells[hi], CompareValueType), t);
            }
        }
        else
        {
            CopyCompareValues(Cells, o.Cells, CompareValueType);
        }
        _axis.CompareValueType = CompareValueType;
        CopyCompareValues(_axis.Cells, o._axis.Cells, CompareValueType);
        _axis.CalcCellRanges();
    }

    public override string GetTableAsString() => _axis.GetTableAsString() + Environment.NewLine + base.GetTableAsString();
}

/// <summary>A 3D table: X axis by Y axis grid of data (a map).</summary>
public class Table3D : Table
{
    private Table1D _xAxis;
    private Table1D _yAxis;
    private DataCell[,] _data = new DataCell[0, 0];

    public Table3D()
    {
        _xAxis = new Table1D { AxisKind = AxisKind.X, AxisParent = this };
        _yAxis = new Table1D { AxisKind = AxisKind.Y, AxisParent = this };
    }

    public override TableType Type => TableType.Table3D;

    public Table1D XAxis
    {
        get => _xAxis;
        set
        {
            _xAxis = value;
            value.AxisParent = this;
            value.AxisKind = AxisKind.X;
        }
    }

    public Table1D YAxis
    {
        get => _yAxis;
        set
        {
            _yAxis = value;
            value.AxisParent = this;
            value.AxisKind = AxisKind.Y;
        }
    }

    public int SizeX { get; set; } = 1;
    public int SizeY { get; set; } = 1;
    public bool SwapXY { get; set; }
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }

    /// <summary>Unused cells between the end of one row/column and the start of the next.</summary>
    public int SkipCells { get; set; }

    /// <summary>Cells indexed [x, y].</summary>
    public DataCell[,] Data3D => _data;

    public override IEnumerable<DataCell> DataCells
    {
        get
        {
            for (int y = 0; y < _data.GetLength(1); y++)
                for (int x = 0; x < _data.GetLength(0); x++)
                    yield return _data[x, y];
        }
    }

    public override IEnumerable<Table1D> Axes => [_xAxis, _yAxis];

    public override Table Clone()
    {
        var copy = (Table3D)base.Clone();
        copy.XAxis = (Table1D)_xAxis.Clone();
        copy.YAxis = (Table1D)_yAxis.Clone();
        copy._data = new DataCell[0, 0];
        return copy;
    }

    public override void Populate(Rom rom, IList<string> warnings)
    {
        ValidateScaling(warnings);
        InitRamOffset(rom);

        int sizeX = SizeX;
        int sizeY = SizeY;
        _xAxis.DataSize = sizeX;
        _yAxis.DataSize = sizeY;
        _xAxis.Populate(rom, warnings);
        _yAxis.Populate(rom, warnings);

        _data = new DataCell[sizeX, sizeY];

        int offset = 0;
        int iMax = SwapXY ? sizeX : sizeY;
        int jMax = SwapXY ? sizeY : sizeX;
        for (int i = 0; i < iMax; i++)
        {
            for (int j = 0; j < jMax; j++)
            {
                // RomRaider's flip naming: flipy reverses the inner index, flipx the outer.
                int x = FlipY ? jMax - j - 1 : j;
                int y = FlipX ? iMax - i - 1 : i;
                if (SwapXY)
                    (x, y) = (y, x);

                var cell = new DataCell(this, rom, offset);
                rom.RegisterCell(cell);
                _data[x, y] = cell;
                offset += j == jMax - 1 ? 1 + SkipCells : 1;
            }
        }

        Cells = DataCells.ToArray();
        CalcCellRanges();
        AddScale(new Scale());
        SyncAxisScale(_xAxis, CurrentScale);
        SyncAxisScale(_yAxis, CurrentScale);
    }

    public override void SetCurrentScale(Scale scale)
    {
        SyncAxisScale(_xAxis, scale);
        SyncAxisScale(_yAxis, scale);
        base.SetCurrentScale(scale);
    }

    private (int x1, int y1, int x2, int y2)? SelectionBounds()
    {
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = -1, y2 = -1;
        for (int x = 0; x < _data.GetLength(0); x++)
        {
            for (int y = 0; y < _data.GetLength(1); y++)
            {
                if (!_data[x, y].IsSelected)
                    continue;
                x1 = Math.Min(x1, x);
                x2 = Math.Max(x2, x);
                y1 = Math.Min(y1, y);
                y2 = Math.Max(y2, y);
            }
        }
        return x2 < 0 ? null : (x1, y1, x2, y2);
    }

    /// <summary>Interpolates down each selected column, along the Y axis.</summary>
    public override void VerticalInterpolate()
    {
        var b = SelectionBounds();
        if (b is { } s && s.y2 - s.y1 > 1)
        {
            var axis = _yAxis.Cells;
            double startX = Table2D.AxisPosition(axis, s.y1);
            double endX = Table2D.AxisPosition(axis, s.y2);
            for (int i = s.x1; i <= s.x2; i++)
            {
                double startY = _data[i, s.y1].BinValue;
                double endY = _data[i, s.y2].BinValue;
                for (int j = s.y1 + 1; j < s.y2; j++)
                    SetInterpolated(_data[i, j], LinearInterpolation(Table2D.AxisPosition(axis, j), startX, endX, startY, endY));
            }
        }
        _yAxis.VerticalInterpolate();
    }

    /// <summary>Interpolates along each selected row, along the X axis.</summary>
    public override void HorizontalInterpolate()
    {
        var b = SelectionBounds();
        if (b is { } s && s.x2 - s.x1 > 1)
        {
            var axis = _xAxis.Cells;
            double startX = Table2D.AxisPosition(axis, s.x1);
            double endX = Table2D.AxisPosition(axis, s.x2);
            for (int i = s.y1; i <= s.y2; i++)
            {
                double startY = _data[s.x1, i].BinValue;
                double endY = _data[s.x2, i].BinValue;
                for (int j = s.x1 + 1; j < s.x2; j++)
                    SetInterpolated(_data[j, i], LinearInterpolation(Table2D.AxisPosition(axis, j), startX, endX, startY, endY));
            }
        }
        _xAxis.HorizontalInterpolate();
    }

    public override void Interpolate()
    {
        VerticalInterpolate();
        HorizontalInterpolate();
    }

    public override bool IsCompatibleWith(Table other) =>
        other is Table3D o &&
        o._data.GetLength(0) == _data.GetLength(0) && o._data.GetLength(1) == _data.GetLength(1) &&
        o._xAxis.Cells.Length == _xAxis.Cells.Length && o._yAxis.Cells.Length == _yAxis.Cells.Length;

    protected override void PopulateCompareValues(Table other)
    {
        if (!IsCompatibleWith(other))
            return;
        var o = (Table3D)other;
        int sizeX = _data.GetLength(0), sizeY = _data.GetLength(1);
        if (InterpolateCompare && _xAxis.Cells.Length == sizeX && _yAxis.Cells.Length == sizeY)
        {
            // Bilinear: along X on the two rows either side, then between those along Y.
            var (mineX, theirX) = LookupAxes(_xAxis, o._xAxis, CompareValueType);
            var (mineY, theirY) = LookupAxes(_yAxis, o._yAxis, CompareValueType);
            var rows = mineY.Select(v => Bracket(theirY, v)).ToArray();
            double Theirs(int x, int y) => CompareSource(o._data[x, y], CompareValueType);
            for (int x = 0; x < sizeX; x++)
            {
                var (x0, x1, tx) = Bracket(theirX, mineX[x]);
                for (int y = 0; y < sizeY; y++)
                {
                    var (y0, y1, ty) = rows[y];
                    _data[x, y].CompareToValue = Lerp(Lerp(Theirs(x0, y0), Theirs(x1, y0), tx), Lerp(Theirs(x0, y1), Theirs(x1, y1), tx), ty);
                }
            }
        }
        else
        {
            for (int x = 0; x < sizeX; x++)
                for (int y = 0; y < sizeY; y++)
                    _data[x, y].CompareToValue = CompareSource(o._data[x, y], CompareValueType);
        }
        foreach (var (mine, theirs) in new[] { (_xAxis, o._xAxis), (_yAxis, o._yAxis) })
        {
            mine.CompareValueType = CompareValueType;
            CopyCompareValues(mine.Cells, theirs.Cells, CompareValueType);
            mine.CalcCellRanges();
        }
    }

    public override string GetTableAsString()
    {
        var sb = new StringBuilder();
        sb.Append(_xAxis.GetTableAsString());
        sb.Append(Environment.NewLine);
        int sizeX = _data.GetLength(0);
        int sizeY = _data.GetLength(1);
        for (int y = 0; y < sizeY; y++)
        {
            var yCell = _yAxis.Cells[y];
            sb.Append(yCell.IsStatic ? yCell.StaticText : ValueFormatter.ToClipboardString(yCell.RealValue));
            sb.Append('\t');
            for (int x = 0; x < sizeX; x++)
            {
                sb.Append(ValueFormatter.ToClipboardString(_data[x, y].RealValue));
                if (x < sizeX - 1)
                    sb.Append('\t');
            }
            if (y < sizeY - 1)
                sb.Append(Environment.NewLine);
        }
        return sb.ToString();
    }
}

/// <summary>A switch: named byte patterns (states) written to <see cref="Table.DataSize"/> bytes.</summary>
public class TableSwitch : Table1D
{
    public TableSwitch()
    {
        StorageType = 1;
    }

    public override TableType Type => TableType.Switch;

    /// <summary>The state whose bytes match the image, if any.</summary>
    public PresetEntry? ActiveState =>
        Presets?.Presets.FirstOrDefault(p => PresetManager.IsActive(this, p));
}

/// <summary>A bitwise switch: each named bit of one value is a check box.</summary>
public class TableBitwiseSwitch : TableSwitch
{
    private List<int> _bits = [];

    public IReadOnlyList<int> Bits => _bits;

    public override Table Clone()
    {
        var copy = (TableBitwiseSwitch)base.Clone();
        copy._bits = [.. _bits];
        return copy;
    }

    public override void SetPresetValues(string name, string bitPos)
    {
        if (string.IsNullOrEmpty(bitPos))
            return;
        _bits.Add(int.Parse(bitPos, System.Globalization.CultureInfo.InvariantCulture));
        SetPresetValues(name, "1", _bits.Count - 1);
    }

    public override void Populate(Rom rom, IList<string> warnings)
    {
        InitRamOffset(rom);
        DataSize = _bits.Count;
        Cells = new DataCell[_bits.Count];
        for (int i = 0; i < _bits.Count; i++)
        {
            // Every bit reads the same address with its own mask.
            Cells[i] = new DataCell(this, rom, i, MaskUtil.BitToMask(_bits[i]));
            rom.RegisterCell(Cells[i]);
        }
        CalcCellRanges();
        AddScale(new Scale());
    }
}
