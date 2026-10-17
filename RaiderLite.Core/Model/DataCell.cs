namespace RaiderLite.Core.Model;

/// <summary>
/// One value of a table. Values are read from and written straight through to the
/// ROM image. Port of RomRaider's DataCell.
/// </summary>
public sealed class DataCell
{
    private bool _selected;
    private double _minAllowedBin;
    private double _maxAllowedBin;
    private bool _rangeComputed;

    // Cells only exist once their table has been read from an image, so unlike Table.Rom
    // (null while a definition is being parsed) this is never null.
    private readonly Rom _rom;

    /// <summary>Creates a static (label) cell for a static axis.</summary>
    internal DataCell(Table table, Rom rom, int index, string staticText)
    {
        Table = table;
        _rom = rom;
        Index = index;
        BitMask = table.BitMask;
        var tokens = staticText.Split(['\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries);
        StaticText = tokens.Length > 0 ? tokens[0] : "";
    }

    /// <summary>Creates a cell backed by the image at <paramref name="index"/>.</summary>
    internal DataCell(Table table, Rom rom, int index, int bitMask = 0)
    {
        Table = table;
        _rom = rom;
        Index = index;
        SetBitMask(table.BitMask);
        SetBitMask(bitMask);
        RefreshFromMemory();
        OriginalValue = BinValue;
    }

    public Table Table { get; }

    /// <summary>Position of this cell's value in the table's storage order.</summary>
    public int Index { get; }

    public double BinValue { get; private set; }

    /// <summary>Value at load time or at the last revert point.</summary>
    public double OriginalValue { get; internal set; }

    /// <summary>Raw value of the corresponding cell in the compared table.</summary>
    public double CompareToValue { get; internal set; }

    public int BitMask { get; private set; }

    /// <summary>Label text, for cells of a static axis; null otherwise.</summary>
    public string? StaticText { get; }

    public bool IsStatic => StaticText != null;

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (!IsStatic)
                _selected = value;
        }
    }

    public double MinAllowedBin
    {
        get
        {
            EnsureRange();
            return _minAllowedBin;
        }
    }

    public double MaxAllowedBin
    {
        get
        {
            EnsureRange();
            return _maxAllowedBin;
        }
    }

    public bool IsOutOfRange => !IsStatic && (BinValue > MaxAllowedBin || BinValue < MinAllowedBin);

    public bool IsChanged => !IsStatic && BinValue != OriginalValue;

    public double RealValue => Table.CurrentScale.ToReal(BinValue);

    public double CompareValue => BinValue - CompareToValue;

    public double RealCompareValue
    {
        get
        {
            var scale = Table.CurrentScale;
            return scale.ToReal(BinValue) - scale.ToReal(CompareToValue);
        }
    }

    /// <summary>Relative change against the compared value: (V2 - V1) / |V1|.</summary>
    public double RealCompareChangeValue
    {
        get
        {
            var scale = Table.CurrentScale;
            double real = scale.ToReal(BinValue);
            double compare = scale.ToReal(CompareToValue);
            return compare != 0.0 ? (real - compare) / Math.Abs(compare) : real - compare;
        }
    }

    /// <summary>Byte offset of this cell's value in the image.</summary>
    public int Address
    {
        get
        {
            int index = Table is TableBitwiseSwitch ? 0 : Index;
            return Table.StorageAddress + index * StorageType.ByteLength(Table.StorageType) - Table.RamOffset;
        }
    }

    internal void SetBitMask(int mask)
    {
        if (mask == 0)
            return;
        int bytes = StorageType.ByteLength(Table.StorageType);
        long max = bytes >= 4 ? uint.MaxValue : (1L << (bytes * 8)) - 1;
        BitMask = (int)Math.Min((uint)mask, max);
        _rangeComputed = false;
    }

    private void EnsureRange()
    {
        if (_rangeComputed)
            return;
        _rangeComputed = true;
        int st = Table.StorageType;
        if (st == StorageType.Float)
        {
            _maxAllowedBin = float.MaxValue;
            _minAllowedBin = -float.MaxValue;
        }
        else if (Table.Signed && BitMask == 0)
        {
            (_minAllowedBin, _maxAllowedBin) = st switch
            {
                1 => ((double)sbyte.MinValue, (double)sbyte.MaxValue),
                2 => (short.MinValue, short.MaxValue),
                4 => (int.MinValue, int.MaxValue),
                StorageType.Movi20 => (StorageType.Movi20MinValue, StorageType.Movi20MaxValue),
                StorageType.Movi20S => (StorageType.Movi20SMinValue, StorageType.Movi20SMaxValue),
                _ => (0, 0),
            };
        }
        else
        {
            _maxAllowedBin = BitMask == 0
                ? Math.Pow(256, StorageType.ByteLength(st)) - 1
                : Math.Pow(2, MaskUtil.LengthOfMask(BitMask)) - 1;
            _minAllowedBin = 0.0;
        }
    }

    private double ReadValue(int index)
    {
        var input = _rom.Binary;
        int st = Table.StorageType;
        int address = Table.StorageAddress + index * StorageType.ByteLength(st) - Table.RamOffset;

        if (st == StorageType.Float)
            return ByteCodec.ReadFloat(input, address, Table.MemModelEndian);
        if (BitMask == 0 || st is StorageType.Movi20 or StorageType.Movi20S)
            return ByteCodec.ReadInteger(input, Table.Endian, address, st, Table.Signed);
        return ByteCodec.ReadMasked(input, Table.Endian, address, st, Table.Signed, BitMask);
    }

    private double ReadValue()
    {
        int index = Table is TableBitwiseSwitch ? 0 : Index;
        if (Table.DataLayout == DataLayout.BoschSubtract)
        {
            // Bosch Motronic subtract layout: value = 2^bits - sum of stored values from here to the end.
            double value = Math.Pow(2, 8 * Table.StorageType);
            for (int j = Table.DataSize - 1; j >= index; j--)
                value -= ReadValue(j);
            return value;
        }
        return ReadValue(index);
    }

    /// <summary>Re-reads the value from the image (e.g. after an aliased cell was written).</summary>
    internal void RefreshFromMemory()
    {
        BinValue = ReadValue();
    }

    private void WriteToImage()
    {
        // Whether a table may be edited is decided before a value is accepted (SetBinValue): once it
        // is, the bytes are always written, so undo/redo can't leave the image and the values apart.
        if (Table.IsChecksumFixTable)
            return;

        var binData = _rom.Binary;
        int st = Table.StorageType;
        int ramOffset = Table.RamOffset;
        int storageAddress = Table.StorageAddress;
        int index = Table is TableBitwiseSwitch ? 0 : Index;
        bool boschSubtract = Table.DataLayout == DataLayout.BoschSubtract;

        if (st == StorageType.Float)
        {
            var output = ByteCodec.EncodeFloat((float)BinValue, Table.MemModelEndian);
            Array.Copy(output, 0, binData, storageAddress + index * 4 - ramOffset, 4);
        }
        else
        {
            double value = BinValue;
            if (boschSubtract)
            {
                // Reverse the cross referencing: stored = 2^bits - value - sum(stored values after this one).
                double crossed = Math.Pow(2, 8 * st) - BinValue;
                for (int i = Table.DataSize - 1; i > index; i--)
                    crossed -= ReadValue(i);
                value = crossed;
            }

            int finalValue = (int)(long)value;
            if (st is StorageType.Movi20 or StorageType.Movi20S)
            {
                ByteCodec.WriteMovi20(binData, index * 3 + storageAddress - ramOffset, finalValue);
                return;
            }
            int mask = BitMask;
            if (mask != 0)
                finalValue <<= MaskUtil.FirstOneOfMask(mask);

            var output = ByteCodec.EncodeInteger(finalValue, Table.Endian, st);
            int byteLength = StorageType.ByteLength(st);
            int start = index * byteLength + storageAddress - ramOffset;

            if (mask != 0)
            {
                // Only change the bits inside the mask.
                var maskBytes = ByteCodec.EncodeInteger(mask, Table.Endian, st);
                for (int z = 0; z < byteLength; z++)
                {
                    binData[start + z] = (byte)((binData[start + z] & ~maskBytes[z]) | (output[z] & maskBytes[z]));
                }
            }
            else
            {
                Array.Copy(output, 0, binData, start, byteLength);
            }
        }

        // In the Bosch subtract layout every earlier cell depends on this one.
        if (boschSubtract && index > 0)
            Table.Cells[index - 1].WriteToImage();
    }

    /// <summary>Sets the raw value, clamped to the storage range, and writes it to the image.</summary>
    /// <exception cref="UserLevelException">The table's user level is above the current one.</exception>
    public void SetBinValue(double newBinValue)
    {
        if (IsStatic || double.IsNaN(newBinValue) || BinValue == newBinValue || Table.Locked || Table.IsChecksumFixTable)
            return;

        if (Table.UserLevel > CoreSettings.Current.UserLevel)
            throw new UserLevelException(Table.UserLevel);
        if (Table.UserLevel >= 5 && !CoreSettings.Current.SaveDebugTables)
            throw new UserLevelException(Table.UserLevel, "Debug (level 5) tables are read-only while \"Save debug tables\" is off in Settings.");

        double checkedValue = Representable(Math.Clamp(newBinValue, MinAllowedBin, MaxAllowedBin));
        if (BinValue == checkedValue)
            return;

        double old = BinValue;
        BinValue = checkedValue;
        WriteToImage();
        _rom.OnCellWritten(this, old);
    }

    /// <summary>
    /// The nearest value the cell's encoding can store, so the value shown (and recorded for undo)
    /// is always exactly what the image holds.
    /// </summary>
    private double Representable(double value)
    {
        int st = Table.StorageType;
        if (st == StorageType.Float)
            return (float)value;
        if (st is StorageType.Movi20 or StorageType.Movi20S)
        {
            // The instruction's own form decides, as when reading and writing: MOVI20S holds multiples of 256.
            if (ByteCodec.IsMovi20S(_rom.Binary, Address))
                return Math.Clamp(Math.Round(value / 256, MidpointRounding.AwayFromZero) * 256, StorageType.Movi20SMinValue, StorageType.Movi20SMaxValue);
            return Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), StorageType.Movi20MinValue, StorageType.Movi20MaxValue);
        }
        value = Math.Round(value, MidpointRounding.AwayFromZero);
        return Table.DataLayout == DataLayout.BoschSubtract ? BoschRepresentable(value) : value;
    }

    /// <summary>
    /// Bosch subtract layout: value[i] = value[i+1] - stored[i], with value[n] = 2^bits. A value
    /// must stay within one stored step of the next value, and of the previous one (whose stored
    /// value is rewritten to keep it); otherwise the bytes would read back as something else.
    /// </summary>
    private double BoschRepresentable(double value)
    {
        int bits = 8 * StorageType.ByteLength(Table.StorageType);
        double storedMin = Table.Signed ? -Math.Pow(2, bits - 1) : 0;
        double storedMax = Table.Signed ? Math.Pow(2, bits - 1) - 1 : Math.Pow(2, bits) - 1;
        double next = Index + 1 < Table.DataSize ? Table.Cells[Index + 1].BinValue : Math.Pow(2, 8 * Table.StorageType);
        double lo = next - storedMax, hi = next - storedMin;
        if (Index > 0)
        {
            double previous = Table.Cells[Index - 1].BinValue;
            lo = Math.Max(lo, previous + storedMin);
            hi = Math.Min(hi, previous + storedMax);
        }
        return lo <= hi ? Math.Clamp(value, lo, hi) : BinValue;
    }

    /// <summary>Writes a value as part of an undo/redo: no clamping or user level checks.</summary>
    internal void RestoreBinValue(double value)
    {
        if (BinValue == value)
            return;
        double old = BinValue;
        BinValue = value;
        WriteToImage();
        _rom.OnCellWritten(this, old, recordHistory: false);
    }

    /// <summary>Sets the value from real-world text. Invalid numbers and "x" are ignored.</summary>
    public void SetRealValue(string input)
    {
        input = input.Replace(" ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (input.Equals("x", StringComparison.OrdinalIgnoreCase))
            return;
        if (!ValueFormatter.TryParse(input, out double real))
            return;
        SetRealValue(real);
    }

    public void SetRealValue(double real)
    {
        var scale = Table.CurrentScale;
        double result = scale.ToBin(real, Table.StorageType, Table.Signed);
        if (double.IsNaN(result))
            return;
        if (Table.StorageType != StorageType.Float)
            result = Math.Round(result, MidpointRounding.AwayFromZero);
        if (BinValue != result)
            SetBinValue(result);
    }

    /// <summary>Adds <paramref name="increment"/> in real units. Always moves at least one raw step.</summary>
    public void Increment(double increment)
    {
        var scale = Table.CurrentScale;
        double oldValue = RealValue;

        if (scale.CoarseIncrement < 0.0)
            increment = -increment;

        for (int attempt = 0; attempt < 32; attempt++)
        {
            double incResult = scale.ToBin(oldValue + increment, Table.StorageType, Table.Signed);
            if (double.IsNaN(incResult))
                return;

            if (Table.StorageType == StorageType.Float)
            {
                if (BinValue != incResult)
                    SetBinValue(incResult);
                return;
            }

            double rounded = Math.Round(incResult, MidpointRounding.AwayFromZero);
            if (BinValue != rounded)
                SetBinValue(rounded);

            // If the increment is too small to change the raw value, double it and retry.
            bool stuck = oldValue == RealValue &&
                         ((increment > 0 && BinValue < MaxAllowedBin) || (increment < 0 && BinValue > MinAllowedBin));
            if (!stuck)
                return;
            increment *= 2;
        }
    }

    public void Multiply(double factor) => SetRealValue(RealValue * factor);

    /// <summary>Reverts to the value at load time or the last revert point.</summary>
    public void Undo() => SetBinValue(OriginalValue);

    public void SetRevertPoint() => OriginalValue = BinValue;

    public override string ToString() => StaticText ?? RealValue.ToString(System.Globalization.CultureInfo.CurrentCulture);
}
