using System.Buffers.Binary;
using System.Globalization;

namespace RaiderLite.Core.Model;

/// <summary>A named set of raw values: a switch state, a bitwise switch bit, or a preset.</summary>
public sealed class PresetEntry
{
    public required string Name { get; init; }

    /// <summary>Index of the first cell the values apply to.</summary>
    public int DataCellOffset { get; init; }

    /// <summary>True for bitwise switch bits: unchecking clears the bit.</summary>
    public bool IsBitMask { get; init; }

    public required IReadOnlyList<long> Data { get; init; }

    /// <summary>Raw text of the definition's data attribute (kept for display).</summary>
    public string DataText { get; init; } = "";
}

/// <summary>Port of RomRaider's PresetManager.</summary>
public sealed class PresetManager
{
    private readonly List<PresetEntry> _presets = [];

    public IReadOnlyList<PresetEntry> Presets => _presets;

    public PresetManager Clone()
    {
        var copy = new PresetManager();
        copy._presets.AddRange(_presets);
        return copy;
    }

    private static long ParseValue(string s, Table table)
    {
        uint u = Convert.ToUInt32(s.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
        if (table.StorageType > 1 && table.Endian == Endian.Little)
        {
            if (table.StorageType == 2)
                u = BinaryPrimitives.ReverseEndianness((ushort)(u & 0xFFFF));
            else if (table.StorageType == 4)
                u = BinaryPrimitives.ReverseEndianness(u);
        }
        if (!table.Signed || table.Cells.Length > 0 && table.Cells[0].BitMask != 0)
            return u;
        return table.StorageType switch
        {
            1 => (sbyte)(byte)u,
            2 => (short)(ushort)u,
            _ => (int)u,
        };
    }

    private static PresetEntry Create(Table table, string name, string data, int dataCellOffset, bool isBitMask)
    {
        data = data.Trim();
        var values = new List<long>();
        foreach (var s in data.Split(data.Contains(',') ? ',' : ' '))
        {
            if (s.Trim().Length > 0)
                values.Add(ParseValue(s, table));
        }
        return new PresetEntry
        {
            Name = name,
            Data = values,
            DataCellOffset = dataCellOffset,
            IsBitMask = isBitMask,
            DataText = data,
        };
    }

    /// <summary>Adds without checking for duplicate names.</summary>
    public void SetPresetValues(Table table, string name, string data, int dataCellOffset, bool isBitMask) =>
        _presets.Add(Create(table, name, data, dataCellOffset, isBitMask));

    /// <summary>Replaces an entry with the same name (case-insensitive) or adds a new one.</summary>
    public void AddPresetValue(Table table, string name, string data, int dataCellOffset, bool isBitMask)
    {
        var entry = Create(table, name, data, dataCellOffset, isBitMask);
        int existing = _presets.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
            _presets[existing] = entry;
        else
            _presets.Add(entry);
    }

    private static bool Fits(Table table, PresetEntry entry) =>
        entry.Data.Count > 0 && table.DataSize >= entry.Data.Count + entry.DataCellOffset && table.Cells.Length >= entry.Data.Count + entry.DataCellOffset;

    public static bool IsActive(Table table, PresetEntry entry)
    {
        if (!Fits(table, entry))
            return false;
        for (int i = 0; i < entry.Data.Count; i++)
        {
            if ((long)table.Cells[i + entry.DataCellOffset].BinValue != entry.Data[i])
                return false;
        }
        return true;
    }

    public static void Apply(Table table, PresetEntry entry)
    {
        if (!Fits(table, entry))
            return;
        for (int i = 0; i < entry.Data.Count; i++)
            table.Cells[i + entry.DataCellOffset].SetBinValue(entry.Data[i]);
    }

    public static void Clear(Table table, PresetEntry entry)
    {
        if (!Fits(table, entry))
            return;
        for (int i = 0; i < entry.Data.Count; i++)
            table.Cells[i + entry.DataCellOffset].SetBinValue(0);
    }

    public override string ToString() => string.Join(", ", _presets.Select(p => p.Name.ToString(CultureInfo.InvariantCulture)));
}
