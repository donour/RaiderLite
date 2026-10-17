using System.Text;

namespace RaiderLite.Core.Model;

/// <summary>Which value of the compared table to show differences against.</summary>
public enum CompareValueType
{
    /// <summary>The other table's current values.</summary>
    Bin,

    /// <summary>The other table's values at load / revert point.</summary>
    Original,
}

/// <summary>
/// Base class for all tables: the definition (address, storage, scales) plus, once
/// populated from an image, its cells. Port of RomRaider's Table.
/// </summary>
public abstract class Table
{
    private string _category = "Other";
    private List<string> _staticTexts = [];

    public string Name { get; set; } = "";

    /// <summary>Category path; "//" separates sub-categories.</summary>
    public string Category
    {
        get => _category;
        set => _category = value.Trim().Replace(" //", "//").Replace("// ", "//");
    }

    public string Description { get; set; } = "";

    public List<Scale> Scales { get; private set; } = [];

    /// <summary>The scaling values are shown in. A raw scale until the definition adds one.</summary>
    public Scale CurrentScale { get; protected set; } = new();

    public PresetManager Presets { get; private set; } = new();

    public int BitMask { get; private set; }

    public int StorageAddress { get; set; }

    public int StorageType { get; set; }

    public bool Signed { get; set; }

    public Endian Endian { get; set; } = Endian.Big;

    /// <summary>Memory model byte order from the romid, overriding table endian when set.</summary>
    public Endian? MemModelEndian { get; set; }

    public bool Flip { get; set; }

    public DataLayout DataLayout { get; set; } = DataLayout.Default;

    /// <summary>Number of values (cells) in a 1D/2D table or axis.</summary>
    public int DataSize { get; set; } = 1;

    public DataCell[] Cells { get; protected set; } = [];

    public bool BeforeRam { get; set; }

    public int RamOffset { get; protected set; }

    public int UserLevel { get; set; }

    public bool Locked { get; set; }

    /// <summary>Why the values can't be changed right now, or null if they can.</summary>
    public string? ReadOnlyReason
    {
        get
        {
            var settings = CoreSettings.Current;
            if (Locked)
                return "locked";
            if (IsChecksumFixTable)
                return "checksum table";
            if (UserLevel > settings.UserLevel)
                return $"requires user level {UserLevel}";
            if (UserLevel >= 5 && !settings.SaveDebugTables)
                return "debug table, read-only (see Settings)";
            return null;
        }
    }

    public string LogParam { get; set; } = "";

    public bool IsStaticDataTable { get; private set; }

    public IReadOnlyList<string> StaticTexts => _staticTexts;

    public Rom? Rom { get; internal set; }

    public abstract TableType Type { get; }

    public bool IsChecksumFixTable => Name.Contains("Checksum Fix", StringComparison.Ordinal);

    /// <summary>Scalar, array, map or switch, judged by the number of values.</summary>
    public TableShape Shape => this switch
    {
        TableSwitch => TableShape.Switch,
        Table3D t3 when t3.SizeX > 1 && t3.SizeY > 1 => TableShape.Map,
        Table3D t3 => t3.SizeX * t3.SizeY == 1 ? TableShape.Scalar : TableShape.Array,
        _ => DataSize <= 1 ? TableShape.Scalar : TableShape.Array,
    };

    /// <summary>Short description of the shape, e.g. "Scalar", "Array (16)", "Map 16 x 8".</summary>
    public string ShapeDescription => Shape switch
    {
        TableShape.Scalar => "Scalar",
        TableShape.Array => $"Array ({(this is Table3D a ? a.SizeX * a.SizeY : DataSize)})",
        TableShape.Map => $"Map {((Table3D)this).SizeX} x {((Table3D)this).SizeY}",
        _ => this is TableBitwiseSwitch ? "Bit switch" : "Switch",
    };

    public Table? CompareTable { get; private set; }

    public CompareValueType CompareValueType { get; set; } = CompareValueType.Bin;

    public double MinBin { get; protected set; }
    public double MaxBin { get; protected set; }
    public double MinCompare { get; protected set; }
    public double MaxCompare { get; protected set; }

    /// <summary>Raised after any cell of this table (or one of its axes) changed.</summary>
    public event EventHandler? DataChanged;

    /// <summary>Raised when the current scale changes.</summary>
    public event EventHandler? ScaleChanged;

    /// <summary>The cells this table owns, excluding axes.</summary>
    public virtual IEnumerable<DataCell> DataCells => Cells;

    /// <summary>Axis tables, if any.</summary>
    public virtual IEnumerable<Table1D> Axes => [];

    /// <summary>Data cells plus axis cells.</summary>
    public IEnumerable<DataCell> AllCells => DataCells.Concat(Axes.SelectMany(a => a.DataCells));

    /// <summary>The table shown in the tree (axes resolve to their parent).</summary>
    public virtual Table TopLevel => this;

    /// <summary>Deep copy of the definition (no cells). Used for <c>base=</c> table inheritance.</summary>
    public virtual Table Clone()
    {
        var copy = (Table)MemberwiseClone();
        copy.DataChanged = null;
        copy.ScaleChanged = null;
        copy.Scales = Scales.Select(s => s.Clone()).ToList();
        int current = Scales.IndexOf(CurrentScale);
        copy.CurrentScale = current >= 0 ? copy.Scales[current] : new Scale();
        copy.Presets = Presets.Clone();
        copy._staticTexts = [.. _staticTexts];
        copy.Cells = [];
        copy.Rom = null;
        copy.CompareTable = null;
        return copy;
    }

    public void AddStaticDataCell(string s)
    {
        // Labels beyond DataSize are ignored when the cells are created.
        IsStaticDataTable = true;
        _staticTexts.Add(s);
    }

    public void ClearStaticData() => _staticTexts.Clear();

    internal void RaiseDataChanged()
    {
        DataChanged?.Invoke(this, EventArgs.Empty);
        if (TopLevel != this)
            TopLevel.RaiseDataChanged();
    }

    internal void RaiseScaleChanged()
    {
        ScaleChanged?.Invoke(this, EventArgs.Empty);
        // An axis changing scale changes how its table is displayed.
        if (TopLevel != this)
            TopLevel.RaiseScaleChanged();
    }

    /// <summary>Reads the cells from the rom's image.</summary>
    /// <exception cref="IndexOutOfRangeException">The table lies outside the image.</exception>
    public virtual void Populate(Rom rom, IList<string> warnings)
    {
        Rom = rom;
        if (IsStaticDataTable)
        {
            Cells = new DataCell[DataSize];
            for (int i = 0; i < DataSize; i++)
                Cells[i] = new DataCell(this, rom, i, i < _staticTexts.Count ? _staticTexts[i] : "");
            return;
        }

        ValidateScaling(warnings);
        if (!BeforeRam)
            RamOffset = rom.RomId.RamOffset;

        Cells = new DataCell[DataSize];
        for (int i = 0; i < DataSize; i++)
        {
            Cells[i] = new DataCell(this, rom, i);
            rom.RegisterCell(Cells[i]);
        }

        CalcCellRanges();
        AddScale(new Scale());
    }

    protected void ValidateScaling(IList<string> warnings)
    {
        if (Type == TableType.Switch)
            return;
        foreach (var scale in Scales)
        {
            if (!scale.Validate())
            {
                warnings.Add($"{Name}: scaling \"{scale.Category}\" expression ({scale.Expression}) and " +
                             $"to_byte ({scale.ByteExpression}) are not inverses; values may change when edited.");
            }
        }
    }

    protected void InitRamOffset(Rom rom)
    {
        Rom = rom;
        if (!BeforeRam)
            RamOffset = rom.RomId.RamOffset;
    }

    public virtual void CalcCellRanges()
    {
        var cells = DataCells.ToList();
        if (cells.Count == 0)
            return;
        MinBin = MaxBin = cells[0].BinValue;
        MinCompare = MaxCompare = cells[0].CompareValue;
        foreach (var cell in cells)
        {
            MaxBin = Math.Max(MaxBin, cell.BinValue);
            MinBin = Math.Min(MinBin, cell.BinValue);
            MaxCompare = Math.Max(MaxCompare, cell.CompareValue);
            MinCompare = Math.Min(MinCompare, cell.CompareValue);
        }
    }

    public double MaxReal => CurrentScale.ToReal(MaxBin);
    public double MinReal => CurrentScale.ToReal(MinBin);

    // ----- scales -----

    /// <summary>Returns the scale with this category, or a new default scale.</summary>
    public Scale GetScale(string category) =>
        Scales.FirstOrDefault(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) ?? new Scale();

    /// <summary>Adds or replaces (by category) a scale, and selects it if it is the preferred one.</summary>
    public void AddScale(Scale scale)
    {
        int existing = Scales.FindIndex(s => s.Category.Equals(scale.Category, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            if (ReferenceEquals(CurrentScale, Scales[existing]))
                CurrentScale = scale;
            Scales.RemoveAt(existing);
        }
        Scales.Add(scale);

        // The initial raw placeholder isn't in Scales: the first scaling added replaces it.
        if (!Scales.Contains(CurrentScale) ||
            CoreSettings.Current.DefaultScale.Equals(scale.Category, StringComparison.OrdinalIgnoreCase) ||
            "Default".Equals(scale.Category, StringComparison.OrdinalIgnoreCase))
        {
            CurrentScale = scale;
        }
    }

    public virtual void SetCurrentScale(Scale scale)
    {
        CurrentScale = scale;
        CalcCellRanges();
        RaiseScaleChanged();
        RaiseDataChanged();
    }

    public bool SetScaleByCategory(string category)
    {
        var scale = Scales.FirstOrDefault(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (scale == null)
            return false;
        if (!ReferenceEquals(scale, CurrentScale))
            SetCurrentScale(scale);
        return true;
    }

    /// <summary>Switches an axis to the same scale category as its table, falling back to defaults.</summary>
    protected static void SyncAxisScale(Table1D axis, Scale scale)
    {
        if (!CoreSettings.Current.ScaleHeadersAndData || axis.IsStaticDataTable)
            return;
        if (!axis.SetScaleByCategory(scale.Category) && !axis.SetScaleByCategory(CoreSettings.Current.DefaultScale))
            axis.SetScaleByCategory("Default");
    }

    // ----- definition helpers -----

    public void SetStringMask(string mask)
    {
        if (mask.Length == 0)
            return;
        SetBitMask((int)Convert.ToUInt32(mask.Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16));
    }

    public void SetBitMask(int mask)
    {
        int bytes = Model.StorageType.ByteLength(StorageType);
        long max = bytes >= 4 ? uint.MaxValue : (1L << (bytes * 8)) - 1;
        BitMask = (int)Math.Min((uint)mask, max);
    }

    /// <summary><c>&lt;bit&gt;</c> element: adds without checking duplicates.</summary>
    public virtual void SetPresetValues(string name, string value)
    {
        Presets.SetPresetValues(this, name, value, 0, false);
    }

    protected void SetPresetValues(string name, string value, int dataCellOffset)
    {
        Presets ??= new PresetManager();
        Presets.SetPresetValues(this, name, value, dataCellOffset, true);
    }

    /// <summary><c>&lt;state&gt;</c> element: replaces a state with the same name.</summary>
    public void AddPresetValue(string name, string value)
    {
        Presets ??= new PresetManager();
        Presets.AddPresetValue(this, name, value, 0, false);
    }

    // ----- editing operations; they act on selected cells -----

    public virtual void ClearSelection()
    {
        foreach (var cell in AllCells)
            cell.IsSelected = false;
    }

    public IEnumerable<DataCell> SelectedCells => AllCells.Where(c => c.IsSelected);

    public bool HasSelection => AllCells.Any(c => c.IsSelected);

    public virtual void SetRealValue(string realValue)
    {
        foreach (var cell in DataCells.Where(c => c.IsSelected).ToList())
            cell.SetRealValue(realValue);
        foreach (var axis in Axes)
            axis.SetRealValue(realValue);
    }

    public virtual void Increment(double increment)
    {
        foreach (var cell in DataCells.Where(c => c.IsSelected).ToList())
            cell.Increment(increment);
        foreach (var axis in Axes)
            axis.Increment(increment);
    }

    public virtual void Multiply(double factor)
    {
        foreach (var cell in DataCells.Where(c => c.IsSelected).ToList())
            cell.Multiply(factor);
        foreach (var axis in Axes)
            axis.Multiply(factor);
    }

    public virtual void Interpolate() => HorizontalInterpolate();

    public virtual void VerticalInterpolate() => HorizontalInterpolate();

    /// <summary>Linear interpolation by index between the first and last selected cells.</summary>
    public virtual void HorizontalInterpolate() => InterpolateByIndex(Cells);

    protected static void InterpolateByIndex(DataCell[] data)
    {
        int first = data.Length, last = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (!data[i].IsSelected)
                continue;
            first = Math.Min(first, i);
            last = Math.Max(last, i);
        }
        if (last - first <= 1)
            return;
        double y1 = data[first].BinValue;
        double y2 = data[last].BinValue;
        for (int i = first + 1; i < last; i++)
        {
            double p = (double)(i - first) / (last - first);
            SetInterpolated(data[i], y2 * p + y1 * (1 - p));
        }
    }

    protected static void SetInterpolated(DataCell cell, double bin)
    {
        if (cell.Table.StorageType != Model.StorageType.Float)
            bin = Math.Round(bin, MidpointRounding.AwayFromZero);
        cell.SetBinValue(bin);
    }

    public static double LinearInterpolation(double x, double x1, double x2, double y1, double y2) =>
        x1 == x2 ? y1 : y1 + (x - x1) * (y2 - y1) / (x2 - x1);

    /// <summary>Reverts selected cells to their original values.</summary>
    public void UndoSelected()
    {
        foreach (var cell in AllCells.Where(c => c.IsSelected).ToList())
            cell.Undo();
    }

    /// <summary>Reverts every cell to its original value.</summary>
    public void UndoAll()
    {
        foreach (var cell in AllCells.ToList())
            cell.Undo();
    }

    public void SetRevertPoint()
    {
        foreach (var cell in AllCells)
            cell.SetRevertPoint();
        RaiseDataChanged();
    }

    public bool IsChanged => AllCells.Any(c => c.IsChanged);

    // ----- compare -----

    /// <summary>
    /// Compares against another table of the same shape (null turns comparing off).
    /// Does not raise <see cref="DataChanged"/>; views refresh themselves.
    /// </summary>
    public void SetCompareTable(Table? other)
    {
        CompareTable = other;
        if (other == null)
        {
            foreach (var cell in AllCells)
                cell.CompareToValue = 0;
        }
        RefreshCompare();
    }

    /// <summary>Shows changes against this table's own original values.</summary>
    public void CompareToOriginal()
    {
        CompareValueType = CompareValueType.Original;
        foreach (var axis in Axes)
            axis.CompareValueType = CompareValueType.Original;
        SetCompareTable(this);
    }

    /// <summary>Re-reads the compared values (after either table changed).</summary>
    public void RefreshCompare()
    {
        if (CompareTable != null)
            PopulateCompareValues(CompareTable);
        CalcCellRanges();
    }

    /// <summary>Whether <paramref name="other"/> has the same shape as this table.</summary>
    public virtual bool IsCompatibleWith(Table other) =>
        other.GetType() == GetType() && other.Cells.Length == Cells.Length;

    protected virtual void PopulateCompareValues(Table other)
    {
        if (!IsCompatibleWith(other))
            return;
        CopyCompareValues(Cells, other.Cells, CompareValueType);
    }

    protected static void CopyCompareValues(DataCell[] mine, DataCell[] theirs, CompareValueType type)
    {
        int n = Math.Min(mine.Length, theirs.Length);
        for (int i = 0; i < n; i++)
            mine[i].CompareToValue = type == CompareValueType.Bin ? theirs[i].BinValue : theirs[i].OriginalValue;
    }

    // ----- text -----

    /// <summary>Tab-separated values (RomRaider's getTableAsString).</summary>
    public virtual string GetTableAsString()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Cells.Length; i++)
        {
            sb.Append(Cells[i].IsStatic ? Cells[i].StaticText : ValueFormatter.ToClipboardString(Cells[i].RealValue));
            if (i < Cells.Length - 1)
                sb.Append('\t');
        }
        return sb.ToString();
    }

    public override string ToString() => string.IsNullOrEmpty(Name) ? "Unknown" : Name;
}
