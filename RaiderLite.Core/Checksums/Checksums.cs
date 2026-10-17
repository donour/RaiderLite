using System.Buffers.Binary;
using System.Globalization;
using RaiderLite.Core.Model;

namespace RaiderLite.Core.Checksums;

/// <summary>A checksum algorithm declared by a definition's &lt;checksum type="..."/&gt; element.</summary>
public interface IChecksumManager
{
    string Type { get; }

    int NumberOfChecksums { get; }

    void Configure(IReadOnlyDictionary<string, string> vars);

    /// <summary>Returns how many checksums are correct.</summary>
    int Validate(byte[] data);

    /// <summary>Rewrites checksums and returns how many were updated.</summary>
    int Update(byte[] data);

    /// <summary>Called once the rom's tables are populated.</summary>
    void Attach(Rom rom)
    {
    }
}

public static class ChecksumFactory
{
    /// <summary>Creates the manager for a checksum type, or null if the type is unknown.</summary>
    /// <param name="attributes">All attributes of the checksum element, keys lower-cased.</param>
    public static IChecksumManager? Create(IReadOnlyDictionary<string, string> attributes)
    {
        if (!attributes.TryGetValue("type", out var type))
            return null;
        IChecksumManager? manager = type.ToUpperInvariant() switch
        {
            "STD" => new NissanChecksum("STD", alt2: false),
            "ALT" => new NissanChecksum("ALT", alt2: false),
            "ALT2" => new NissanChecksum("ALT2", alt2: true),
            "BYTEXOR" => new ByteXorChecksum(),
            "COPY" => new CopyChecksum(),
            "MOTRONICSINGLE" => new MotronicChecksum(dual: false),
            "MOTRONICDOUBLE" => new MotronicChecksum(dual: true),
            "E38PCM" => new E38PcmChecksum(),
            _ => null,
        };
        manager?.Configure(attributes);
        return manager;
    }

    internal static int Hex(IReadOnlyDictionary<string, string> vars, string key)
    {
        if (!vars.TryGetValue(key, out var v))
            throw new FormatException($"checksum attribute '{key}' is missing");
        return HexUtil.HexToInt(v);
    }

    internal static int ReadInt32BE(byte[] b, int i) => BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(i, 4));

    internal static short ReadInt16BE(byte[] b, int i) => BinaryPrimitives.ReadInt16BigEndian(b.AsSpan(i, 2));

    internal static void WriteInt32BE(byte[] b, int i, int v) => BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(i, 4), v);

    internal static void WriteInt16BE(byte[] b, int i, short v) => BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(i, 2), v);
}

/// <summary>Nissan "std"/"alt" (32-bit sum + xor) and "alt2" (plus two 16-bit sums).</summary>
public sealed class NissanChecksum(string type, bool alt2) : IChecksumManager
{
    private int _start, _end, _sumLoc, _xorLoc, _skipLoc;
    private int _sumt, _xort;
    private short _calSum, _codeSum;

    public string Type => type;

    public int NumberOfChecksums => alt2 ? 4 : 2;

    public void Configure(IReadOnlyDictionary<string, string> vars)
    {
        _start = ChecksumFactory.Hex(vars, "start");
        _end = ChecksumFactory.Hex(vars, "end");
        _sumLoc = ChecksumFactory.Hex(vars, "sumloc");
        _xorLoc = ChecksumFactory.Hex(vars, "xorloc");
        _skipLoc = vars.ContainsKey("skiploc") ? ChecksumFactory.Hex(vars, "skiploc") : 0x20000;
    }

    private void Calculate(byte[] bin)
    {
        int sumt = 0, xort = 0;
        if (!alt2)
        {
            for (int i = _start; i < _end; i += 4)
            {
                if (i == _sumLoc || i == _xorLoc)
                    continue;
                int dw = ChecksumFactory.ReadInt32BE(bin, i);
                sumt += dw;
                xort ^= dw;
            }
            _sumt = sumt;
            _xort = xort;
            return;
        }

        for (int i = _start + 4; i < _end; i += 4)
        {
            if (i == _sumLoc || i == _xorLoc || i == _skipLoc)
                continue;
            int dw = ChecksumFactory.ReadInt32BE(bin, i);
            sumt += dw;
            xort ^= dw;
        }
        _sumt = sumt;
        _xort = xort;

        // 16-bit calibration checksum, including the new 32-bit sum and xor.
        short sum = 0;
        for (int i = _start + 2; i < _skipLoc; i += 2)
        {
            if (i == _sumLoc || i == _xorLoc)
            {
                int dw = i == _sumLoc ? _sumt : _xort;
                sum += (short)((dw >> 16) & 0xffff);
                sum += (short)(dw & 0xffff);
                i += 2;
                continue;
            }
            sum += ChecksumFactory.ReadInt16BE(bin, i);
        }
        _calSum = sum;

        // 16-bit code checksum.
        sum = 0;
        for (int i = _skipLoc + 2; i < _end; i += 2)
            sum += ChecksumFactory.ReadInt16BE(bin, i);
        _codeSum = sum;
    }

    public int Validate(byte[] data)
    {
        Calculate(data);
        int valid = 0;
        if (_sumt == ChecksumFactory.ReadInt32BE(data, _sumLoc))
            valid++;
        if (_xort == ChecksumFactory.ReadInt32BE(data, _xorLoc))
            valid++;
        if (alt2)
        {
            if (_calSum == ChecksumFactory.ReadInt16BE(data, _start))
                valid++;
            if (_codeSum == ChecksumFactory.ReadInt16BE(data, _skipLoc))
                valid++;
        }
        return valid;
    }

    public int Update(byte[] data)
    {
        Calculate(data);
        ChecksumFactory.WriteInt32BE(data, _sumLoc, _sumt);
        ChecksumFactory.WriteInt32BE(data, _xorLoc, _xort);
        if (alt2)
        {
            ChecksumFactory.WriteInt16BE(data, _start, _calSum);
            ChecksumFactory.WriteInt16BE(data, _skipLoc, _codeSum);
        }
        return NumberOfChecksums;
    }
}

/// <summary>XOR of all bytes in [start, end), stored in one byte.</summary>
public sealed class ByteXorChecksum : IChecksumManager
{
    private int _start, _end, _xorLoc;

    public string Type => "BYTEXOR";

    public int NumberOfChecksums => 1;

    public void Configure(IReadOnlyDictionary<string, string> vars)
    {
        _start = ChecksumFactory.Hex(vars, "start");
        _end = ChecksumFactory.Hex(vars, "end");
        _xorLoc = ChecksumFactory.Hex(vars, "xorloc");
    }

    private byte Calculate(byte[] data)
    {
        byte x = 0;
        for (int i = _start; i < _end; i++)
        {
            if (i != _xorLoc)
                x ^= data[i];
        }
        return x;
    }

    public int Validate(byte[] data) => Calculate(data) == data[_xorLoc] ? 1 : 0;

    public int Update(byte[] data)
    {
        byte x = Calculate(data);
        int updated = data[_xorLoc] != x ? 1 : 0;
        data[_xorLoc] = x;
        return updated;
    }
}

/// <summary>Keeps a copy of [start, end] (inclusive) at loc.</summary>
public sealed class CopyChecksum : IChecksumManager
{
    private int _start, _end, _loc;

    public string Type => "COPY";

    public int NumberOfChecksums => 1;

    public void Configure(IReadOnlyDictionary<string, string> vars)
    {
        _start = ChecksumFactory.Hex(vars, "start");
        _end = ChecksumFactory.Hex(vars, "end");
        _loc = ChecksumFactory.Hex(vars, "loc");
    }

    public int Validate(byte[] data) =>
        data.AsSpan(_start, _end - _start + 1).SequenceEqual(data.AsSpan(_loc, _end - _start + 1)) ? 1 : 0;

    public int Update(byte[] data)
    {
        int needed = 1 - Validate(data);
        if (needed > 0)
            Array.Copy(data, _start, data, _loc, _end - _start + 1);
        return needed;
    }
}

/// <summary>Bosch Motronic 16-bit byte sum over one or two inclusive ranges.</summary>
public sealed class MotronicChecksum(bool dual) : IChecksumManager
{
    private int _start, _end, _loc, _start2, _end2;
    private short _initial;

    public string Type => dual ? "MOTRONICDOUBLE" : "MOTRONICSINGLE";

    public int NumberOfChecksums => 1;

    public void Configure(IReadOnlyDictionary<string, string> vars)
    {
        _start = ChecksumFactory.Hex(vars, "start");
        _end = ChecksumFactory.Hex(vars, "end");
        _loc = ChecksumFactory.Hex(vars, "loc");
        if (vars.ContainsKey("initial"))
            _initial = (short)ChecksumFactory.Hex(vars, "initial");
        if (dual)
        {
            _start2 = ChecksumFactory.Hex(vars, "startsecond");
            _end2 = ChecksumFactory.Hex(vars, "endsecond");
        }
    }

    public static short Calculate(short initial, byte[] data, int start, int end)
    {
        short value = initial;
        for (int i = start; i <= end; i++)
            value += data[i];
        return value;
    }

    private short Calculate(byte[] data)
    {
        short sum = Calculate(_initial, data, _start, _end);
        return dual ? Calculate(sum, data, _start2, _end2) : sum;
    }

    public int Validate(byte[] data) => Calculate(data) == ChecksumFactory.ReadInt16BE(data, _loc) ? 1 : 0;

    public int Update(byte[] data)
    {
        short sum = Calculate(data);
        if (sum == ChecksumFactory.ReadInt16BE(data, _loc))
            return 0;
        ChecksumFactory.WriteInt16BE(data, _loc, sum);
        return 1;
    }
}

/// <summary>GM E38 PCM: per-segment 16-bit sum and CVN (CRC16) for six segments.</summary>
public sealed class E38PcmChecksum : IChecksumManager
{
    private sealed class Segment
    {
        public int Start, End;
        public short LSum, LCvn, CSum, CCvn;
        public bool Calculated;
    }

    private static readonly ushort[] Crc16Table = BuildCrcTable();
    private readonly Segment[] _seg = new Segment[7];

    public string Type => "E38PCM";

    public int NumberOfChecksums => 12;

    public void Configure(IReadOnlyDictionary<string, string> vars)
    {
    }

    private static ushort[] BuildCrcTable()
    {
        // CRC-16/ARC (reflected 0xA001), identical to RomRaider's literal table.
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            int crc = i;
            for (int b = 0; b < 8; b++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1;
            table[i] = (ushort)crc;
        }
        return table;
    }

    private void Calculate(byte[] bin)
    {
        const int index = 0x10000;
        int[] offsets = [0x24, 0x48, 0x6b, 0x8e, 0xb1, 0xd4];
        for (int i = 1; i <= 6; i++)
        {
            _seg[i] = new Segment
            {
                Start = (int)ByteCodec.ReadInteger(bin, Endian.Big, index + offsets[i - 1], 4, false),
                End = (int)ByteCodec.ReadInteger(bin, Endian.Big, index + offsets[i - 1] + 4, 4, false),
            };
        }

        for (int a = 1; a <= 6; a++)
        {
            var s = _seg[a];
            if (s.Start > 0x200000 || s.End > 0x200000 || s.Start % 2 != 0 || s.End % 2 == 0 || s.End - s.Start < 0x24)
                return;
        }

        for (int i = 1; i <= 6; i++)
        {
            var s = _seg[i];
            s.LSum = ChecksumFactory.ReadInt16BE(bin, s.Start);
            s.LCvn = ChecksumFactory.ReadInt16BE(bin, s.Start + 0x1E);
            s.CSum = SegmentSum(bin, s.Start, s.End);
            s.CCvn = SegmentCvn(bin, s.Start, s.End);
            s.Calculated = true;
        }
    }

    private static int GmCrc16(byte[] bin, int init, int s, int e)
    {
        for (int num = s; num <= e; num++)
        {
            int low = init & 0xff;
            int high = (init >> 8) & 0xff;
            init = (Crc16Table[low ^ bin[num]] ^ high) & 0xffff;
        }
        return init;
    }

    private static short SegmentSum(byte[] bin, int s, int e)
    {
        int sum = 0;
        for (int i = s + 2; i <= e; i += 2)
            sum += (int)ByteCodec.ReadInteger(bin, Endian.Big, i, 2, false);
        sum = (((sum & 0xFFFF) ^ 0xFFFF) + 1) & 0xFFFF;
        return (short)sum;
    }

    private static short SegmentCvn(byte[] bin, int s, int e)
    {
        int sum = GmCrc16(bin, 0, s + 2, s + 0x1d);
        sum = GmCrc16(bin, sum, s + 0x20, e);
        sum = ((sum & 0xff) << 8) | ((sum >> 8) & 0xff);
        return (short)sum;
    }

    public int Validate(byte[] data)
    {
        Calculate(data);
        int correct = 0;
        for (int i = 1; i <= 6; i++)
        {
            if (!_seg[i].Calculated)
                continue;
            if (_seg[i].CCvn == _seg[i].LCvn)
                correct++;
            if (_seg[i].LSum == _seg[i].CSum)
                correct++;
        }
        return correct;
    }

    public int Update(byte[] data)
    {
        int corrected = 0;
        // Some checksums depend on each other, so RomRaider runs this twice.
        for (int k = 0; k < 2; k++)
        {
            Calculate(data);
            for (int i = 1; i <= 6; i++)
            {
                var s = _seg[i];
                if (!s.Calculated)
                    continue;
                if (s.CCvn != s.LCvn)
                {
                    corrected++;
                    ChecksumFactory.WriteInt16BE(data, s.Start + 0x1E, s.CCvn);
                }
                if (s.LSum != s.CSum)
                {
                    corrected++;
                    ChecksumFactory.WriteInt16BE(data, s.Start, s.CSum);
                }
            }
        }
        return corrected;
    }
}

/// <summary>
/// Subaru "Checksum Fix" table: a list of 12-byte records (start, end, diff) where
/// diff = 0x5AA5A55A - sum(int32 words in [start, end)). Port of RomRaider's RomChecksum.
/// </summary>
public sealed class SubaruChecksumFix(Table table) : IChecksumManager
{
    private const int CheckTotal = 0x5AA5A55A;

    public string Type => "Checksum Fix";

    public Table Table { get; } = table;

    public int NumberOfChecksums => Table.DataSize / 12;

    /// <summary>Whether the checksums were valid at load (RomRaider only recalculates then).</summary>
    public bool ValidAtLoad { get; private set; }

    public void Configure(IReadOnlyDictionary<string, string> vars)
    {
    }

    public void Attach(Rom rom)
    {
        int result = ValidationResult(rom.Binary);
        ValidAtLoad = result == 0;
        if (result == 0)
            Table.Locked = false;
        else if (result > 0)
            rom.Warnings.Add($"{Table.Name}: checksum {result} is invalid; checksums will not be recalculated on save.");
        else
            rom.Warnings.Add($"{Table.Name}: all checksums are disabled.");
    }

    private int Address => Table.StorageAddress - Table.RamOffset;

    /// <summary>0 = all valid, -1 = all disabled, n &gt; 0 = position of first invalid checksum.</summary>
    public int ValidationResult(byte[] input)
    {
        int offset = Table.RamOffset;
        int start = Address;
        int count = Table.DataSize / 12;
        for (int j = 0; j < count; j++)
        {
            int i = start + j * 12;
            int startAddr = ChecksumFactory.ReadInt32BE(input, i);
            int endAddr = ChecksumFactory.ReadInt32BE(input, i + 4);
            int diff = ChecksumFactory.ReadInt32BE(input, i + 8);
            int off = startAddr == 0 && endAddr == 0 ? 0 : offset;
            startAddr -= off;
            endAddr -= off;
            if (j == 0 && startAddr == 0 && endAddr == 0 && diff == CheckTotal)
                return -1;
            if (CheckTotal - diff - Sum(input, startAddr, endAddr) != 0)
                return j + 1;
        }
        return 0;
    }

    public int Validate(byte[] data)
    {
        int result = ValidationResult(data);
        return result switch
        {
            0 => NumberOfChecksums,
            < 0 => NumberOfChecksums,
            _ => result - 1,
        };
    }

    public int Update(byte[] data)
    {
        if (Table.Locked || !ValidAtLoad)
            return 0;
        WriteEditStamp(data);
        return Recalculate(data);
    }

    /// <summary>Recomputes every record's diff value regardless of lock state.</summary>
    public int Recalculate(byte[] data)
    {
        int offset = Table.RamOffset;
        int start = Address;
        int count = Table.DataSize / 12;
        for (int j = 0; j < count; j++)
        {
            int i = start + j * 12;
            int startAddr = ChecksumFactory.ReadInt32BE(data, i);
            int endAddr = ChecksumFactory.ReadInt32BE(data, i + 4);
            int off = startAddr == 0 && endAddr == 0 ? 0 : offset;
            ChecksumFactory.WriteInt32BE(data, i + 8, CheckTotal - Sum(data, startAddr - off, endAddr - off));
        }
        return count;
    }

    /// <summary>RomRaider stamps the save date (yyMMdd + counter) 204 bytes past the table.</summary>
    private void WriteEditStamp(byte[] data)
    {
        int pos = Address + 204;
        if (pos + 4 > data.Length)
            return;
        byte count = data[pos + 3];
        count = count == 0xFF ? (byte)1 : (byte)(count + 1);
        var stamp = Convert.FromHexString(DateTime.Now.ToString("yyMMdd", CultureInfo.InvariantCulture) + count.ToString("x2", CultureInfo.InvariantCulture));
        Array.Copy(stamp, 0, data, pos, 4);
    }

    private static int Sum(byte[] input, int startAddr, int endAddr)
    {
        int sum = 0;
        for (int i = startAddr; i < endAddr; i += 4)
            sum += ChecksumFactory.ReadInt32BE(input, i);
        return sum;
    }
}
