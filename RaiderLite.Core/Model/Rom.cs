using RaiderLite.Core.Checksums;

namespace RaiderLite.Core.Model;

/// <summary>An opened image: its definition, tables and the raw bytes they edit.</summary>
public sealed class Rom
{
    private readonly List<Table> _tables = [];
    private readonly Dictionary<string, Table> _tablesByName = new(StringComparer.OrdinalIgnoreCase);
    // Every cell under each byte it occupies, so a write can refresh all cells sharing its bytes:
    // tables may alias each other exactly or overlap partly (a uint16 over two uint8 values).
    private readonly Dictionary<int, List<DataCell>> _cellsByByte = [];
    private bool _dirty;

    public Rom(RomId romId)
    {
        RomId = romId;
    }

    public RomId RomId { get; set; }

    /// <summary>The image. Cells write into this array directly.</summary>
    public byte[] Binary { get; private set; } = [];

    public string? DefinitionPath { get; set; }

    public string FilePath { get; set; } = "";

    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Tables in definition order.</summary>
    public IReadOnlyList<Table> Tables => _tables;

    public List<IChecksumManager> ChecksumManagers { get; } = [];

    /// <summary>Tables that could not be read (e.g. outside the image) and were removed.</summary>
    public List<string> FaultyTables { get; } = [];

    /// <summary>Non-fatal problems found while loading.</summary>
    public List<string> Warnings { get; } = [];

    public EditHistory History { get; } = new();

    /// <summary>Checksums that could not be checked or updated in the last validate/save (e.g. range outside the image).</summary>
    public List<string> ChecksumErrors { get; } = [];

    public bool IsDirty
    {
        get => _dirty;
        set
        {
            if (_dirty == value)
                return;
            _dirty = value;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? DirtyChanged;

    public Table? GetTable(string name) => _tablesByName.GetValueOrDefault(name);

    /// <summary>Adds a table, replacing one with the same name (keeping its position).</summary>
    public void AddTable(Table table)
    {
        table.Rom = this;
        if (_tablesByName.TryGetValue(table.Name, out var existing))
        {
            int index = _tables.IndexOf(existing);
            _tables[index] = table;
        }
        else
        {
            _tables.Add(table);
        }
        _tablesByName[table.Name] = table;
    }

    public void RemoveTable(Table table)
    {
        if (_tablesByName.Remove(table.Name, out var existing))
            _tables.Remove(existing);
    }

    /// <summary>Reads every table from <paramref name="binData"/>. Faulty tables are removed.</summary>
    public void Populate(byte[] binData)
    {
        Binary = binData;
        FaultyTables.Clear();
        _cellsByByte.Clear();

        foreach (var table in _tables.ToList())
        {
            if (table.StorageAddress < 0)
            {
                RemoveTable(table);
                continue;
            }

            try
            {
                table.Populate(this, Warnings);
                // A table with no values (size 0, a bitwise switch without bits...) can't be shown or edited.
                if (!table.DataCells.Any())
                {
                    FaultyTables.Add(table.Name);
                    Warnings.Add($"{table.Name}: has no values; table removed.");
                    RemoveTable(table);
                }
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or NotSupportedException or OverflowException)
            {
                FaultyTables.Add(table.Name);
                Warnings.Add($"{table.Name}: could not be read ({ex.Message}); table removed.");
                RemoveTable(table);
            }
        }

        WarnAboutPartialOverlaps();

        // Subaru images carry their checksum list in a table named "Checksum Fix".
        ChecksumManagers.RemoveAll(c => c is SubaruChecksumFix);
        foreach (var table in _tables.Where(t => t.Name.StartsWith("Checksum Fix", StringComparison.OrdinalIgnoreCase)))
            ChecksumManagers.Add(new SubaruChecksumFix(table));

        foreach (var checksum in ChecksumManagers)
        {
            try
            {
                checksum.Attach(this);
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException)
            {
                Warnings.Add($"Checksum {checksum.Type}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Tables that share some but not all of their bytes (a uint16 over a separate uint8 value) are
    /// almost always definition mistakes: editing one silently changes the other. Exact aliases and
    /// masked fields of one value are deliberate and not reported.
    /// </summary>
    private void WarnAboutPartialOverlaps()
    {
        var live = new HashSet<Table>(_tables);
        string NameOf(Table t) => ReferenceEquals(t.TopLevel, t) ? t.Name : $"{t.TopLevel.Name} (axis)";
        var reported = new HashSet<(string, string)>();
        foreach (var (address, cells) in _cellsByByte.OrderBy(kv => kv.Key))
        {
            for (int i = 0; i < cells.Count; i++)
            {
                for (int j = i + 1; j < cells.Count; j++)
                {
                    var a = cells[i];
                    var b = cells[j];
                    if (ReferenceEquals(a.Table, b.Table) || a.Address == b.Address && a.Table.StorageType == b.Table.StorageType)
                        continue;
                    if (!live.Contains(a.Table.TopLevel) || !live.Contains(b.Table.TopLevel))
                        continue;
                    var pair = string.CompareOrdinal(NameOf(a.Table), NameOf(b.Table)) < 0
                        ? (NameOf(a.Table), NameOf(b.Table))
                        : (NameOf(b.Table), NameOf(a.Table));
                    if (reported.Count < 50 && reported.Add(pair))
                        Warnings.Add($"{pair.Item1} and {pair.Item2} partly overlap at 0x{address:X}: editing one changes the other.");
                }
            }
        }
    }

    internal void RegisterCell(DataCell cell)
    {
        int address = cell.Address;
        int length = StorageType.ByteLength(cell.Table.StorageType);
        for (int i = 0; i < length; i++)
        {
            if (!_cellsByByte.TryGetValue(address + i, out var list))
                _cellsByByte[address + i] = list = [];
            list.Add(cell);
        }
    }

    /// <summary>Called after a cell wrote its bytes: keeps aliased cells in sync and notifies views.</summary>
    internal void OnCellWritten(DataCell cell, double oldValue, bool recordHistory = true)
    {
        if (recordHistory)
            History.Record(cell, oldValue, cell.BinValue);

        var changedTables = new HashSet<Table> { cell.Table };
        // The bytes that may have changed: the cell's own, or in the Bosch subtract layout (where a
        // write rewrites the earlier stored values) the whole table's.
        IEnumerable<DataCell> written = cell.Table.DataLayout == DataLayout.BoschSubtract
            ? cell.Table.Cells.Where(c => !c.IsStatic)
            : [cell];
        var aliases = new HashSet<DataCell>();
        int length = StorageType.ByteLength(cell.Table.StorageType);
        foreach (var w in written)
        {
            for (int i = 0; i < length; i++)
            {
                if (_cellsByByte.TryGetValue(w.Address + i, out var sharing))
                    aliases.UnionWith(sharing);
            }
        }
        aliases.Remove(cell);
        foreach (var alias in aliases)
        {
            alias.RefreshFromMemory();
            changedTables.Add(alias.Table);
        }

        IsDirty = true;
        foreach (var table in changedTables)
        {
            table.CalcCellRanges();
            table.RaiseDataChanged();
        }
    }

    /// <summary>Total number of checksums the definition declares.</summary>
    public int TotalChecksums => ChecksumManagers.Sum(c => c.NumberOfChecksums);

    /// <summary>Number of checksums that are currently correct.</summary>
    public int ValidateChecksums()
    {
        ChecksumErrors.Clear();
        int valid = 0;
        foreach (var checksum in ChecksumManagers)
        {
            try
            {
                valid += checksum.Validate(Binary);
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                ChecksumErrors.Add($"Checksum {checksum.Type}: {ex.Message}");
            }
        }
        return valid;
    }

    /// <summary>Updates checksums and returns the bytes to write.</summary>
    public byte[] PrepareForSave(out int checksumsUpdated)
    {
        checksumsUpdated = 0;
        ChecksumErrors.Clear();
        foreach (var checksum in ChecksumManagers)
        {
            try
            {
                checksumsUpdated += checksum.Update(Binary);
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                // A checksum whose range doesn't fit the image can't be updated; report it.
                ChecksumErrors.Add($"Checksum {checksum.Type}: {ex.Message}");
            }
        }

        // Checksum updates may have changed bytes behind displayed cells.
        if (checksumsUpdated > 0)
            RefreshAllCells();
        return Binary;
    }

    /// <summary>Re-reads every cell from the image.</summary>
    public void RefreshAllCells()
    {
        foreach (var table in _tables)
        {
            foreach (var cell in table.AllCells)
            {
                if (!cell.IsStatic)
                    cell.RefreshFromMemory();
            }
            table.CalcCellRanges();
            table.RaiseDataChanged();
        }
    }

    public override string ToString() => string.IsNullOrEmpty(FileName) ? RomId.XmlId : $"{RomId.XmlId} - {FileName}";
}
