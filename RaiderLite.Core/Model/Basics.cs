using System.Globalization;

namespace RaiderLite.Core.Model;

public enum Endian
{
    Little,
    Big,
}

public enum TableType
{
    Table1D,
    Table2D,
    Table3D,
    Switch,
}

/// <summary>
/// What a table holds, by shape rather than declared type: definitions often declare a
/// single value as a "2D" table with one static axis entry.
/// </summary>
public enum TableShape
{
    /// <summary>One value.</summary>
    Scalar,

    /// <summary>A row of values: a 1D array or a 2D curve.</summary>
    Array,

    /// <summary>A grid of values (3D table).</summary>
    Map,

    /// <summary>A switch or bitwise switch.</summary>
    Switch,
}

public enum DataLayout
{
    Default,
    BoschSubtract,
}

public enum AxisKind
{
    None,
    X,
    Y,
}

/// <summary>Storage type codes. Integer types are their byte length (1, 2, 4).</summary>
public static class StorageType
{
    public const int Movi20 = 20;
    public const int Movi20S = 28;
    public const int Float = 99;

    public const int Movi20MinValue = unchecked((int)0xfff80000);
    public const int Movi20MaxValue = 0x0007ffff;
    public const int Movi20SMinValue = unchecked((int)0xf8000000);
    public const int Movi20SMaxValue = 0x07ffff00;

    public static int Parse(string input)
    {
        if (input.Equals("movi20", StringComparison.OrdinalIgnoreCase))
            return Movi20;
        if (input.Equals("movi20s", StringComparison.OrdinalIgnoreCase))
            return Movi20S;
        if (input.Equals("float", StringComparison.OrdinalIgnoreCase))
            return Float;
        if (input.StartsWith("uint", StringComparison.OrdinalIgnoreCase))
            return int.Parse(input[4..], CultureInfo.InvariantCulture) / 8;
        if (input.StartsWith("int", StringComparison.OrdinalIgnoreCase))
            return int.Parse(input[3..], CultureInfo.InvariantCulture) / 8;
        return int.Parse(input, CultureInfo.InvariantCulture);
    }

    public static bool IsSigned(string input) =>
        input.StartsWith("int", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("movi20", StringComparison.OrdinalIgnoreCase);

    /// <summary>Number of bytes one value occupies in the image.</summary>
    public static int ByteLength(int storageType) => storageType switch
    {
        Float => 4,
        Movi20 or Movi20S => 3,
        _ => storageType,
    };

    public static string Describe(int storageType, bool signed) => storageType switch
    {
        Float => "float",
        Movi20 => "movi20",
        Movi20S => "movi20s",
        _ => (signed ? "int" : "uint") + (storageType * 8).ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>Editor-wide options the model consults (RomRaider's SettingsManager equivalent).</summary>
public sealed class CoreSettings
{
    public static CoreSettings Current { get; set; } = new();

    private int _userLevel = 1;

    /// <summary>1 (beginner) .. 5 (debug). Tables above this level cannot be edited.</summary>
    public int UserLevel
    {
        get => _userLevel;
        set => _userLevel = Math.Clamp(value, 1, 5);
    }

    /// <summary>Whether level-5 (debug) tables can be changed (edits go straight to the image, so
    /// "not saved" means read-only).</summary>
    public bool SaveDebugTables { get; set; } = true;

    /// <summary>Preferred scaling category, selected when a table defines it.</summary>
    public string DefaultScale { get; set; } = "Metric";

    /// <summary>Switch axis scalings along with the data scaling.</summary>
    public bool ScaleHeadersAndData { get; set; } = true;
}

public sealed class UserLevelException(int level, string? message = null)
    : Exception(message ?? $"This table requires user level {level} to modify.")
{
    public int Level { get; } = level;
}

/// <summary>Hex helpers matching RomRaider's HexUtil.</summary>
public static class HexUtil
{
    /// <summary>Parses a hex string with or without a 0x prefix. "-1" parses as -1.</summary>
    /// <exception cref="FormatException">Not a hex number (including empty), or too large.</exception>
    public static int HexToInt(string input) =>
        TryHexToInt(input, out int value) ? value : throw new FormatException($"\"{input}\" is not a hex number.");

    public static bool TryHexToInt(string? input, out int value)
    {
        value = 0;
        if (input == null)
            return false;
        var s = input.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        bool negative = s.StartsWith('-');
        if (negative)
            s = s[1..];
        if (!uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint u))
            return false;
        value = negative ? -(int)u : (int)u;
        return true;
    }

    public static string ToHex(int value, int minDigits = 0) =>
        "0x" + value.ToString("X" + (minDigits > 0 ? minDigits.ToString(CultureInfo.InvariantCulture) : ""), CultureInfo.InvariantCulture);

    public static byte[] HexStringToBytes(string hex)
    {
        hex = hex.Replace(" ", "", StringComparison.Ordinal);
        if (hex.Length % 2 != 0)
            hex = "0" + hex;
        return Convert.FromHexString(hex);
    }
}

/// <summary>Bit mask helpers matching RomRaider's ByteUtil.</summary>
public static class MaskUtil
{
    /// <summary>Index of the lowest set bit, or -1 if the mask is zero.</summary>
    public static int FirstOneOfMask(int mask)
    {
        for (int i = 0; i < 32; i++)
        {
            if (((mask >> i) & 1) == 1)
                return i;
        }
        return -1;
    }

    /// <summary>Number of set bits.</summary>
    public static int LengthOfMask(int mask) => System.Numerics.BitOperations.PopCount((uint)mask);

    public static int BitToMask(int bit) => 1 << bit;
}
