using System.Globalization;
using System.Text.RegularExpressions;

namespace RaiderLite.Core.Model;

/// <summary>
/// Formats and parses cell values. Scaling formats are Java DecimalFormat patterns
/// ("0.00", "#.##", "#,##0"), which map almost one-to-one onto .NET custom numeric
/// formats. A printf-style pattern such as "0x%04X" is also accepted.
/// </summary>
public static partial class ValueFormatter
{
    [GeneratedRegex(@"%([-+ 0#]*)(\d*)(?:\.(\d+))?([dixXfeEgG])")]
    private static partial Regex PrintfSpec();

    public static string Format(double value, string? pattern, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (double.IsNaN(value))
            return "NaN";
        if (double.IsInfinity(value))
            return value > 0 ? "∞" : "-∞";
        if (string.IsNullOrEmpty(pattern))
            pattern = "#.##";

        var m = PrintfSpec().Match(pattern);
        if (m.Success)
            return pattern[..m.Index] + FormatPrintf(value, m, culture) + pattern[(m.Index + m.Length)..];

        string result;
        try
        {
            result = value.ToString(pattern, culture);
        }
        catch (FormatException)
        {
            result = value.ToString("0.##", culture);
        }

        // DecimalFormat never produces an empty string for zero ("#.##" -> "0").
        if (result.Length == 0 || result == culture.NumberFormat.NegativeSign)
            result = "0";
        return result;
    }

    private static string FormatPrintf(double value, Match m, CultureInfo culture)
    {
        string flags = m.Groups[1].Value;
        // Bounded so a malformed definition format can't overflow or build huge strings.
        int width = int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int w) ? Math.Min(w, 32) : 0;
        int? precision = m.Groups[3].Success && m.Groups[3].Value.Length > 0
            ? (int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int p) ? Math.Min(p, 15) : 15)
            : null;
        char conv = m.Groups[4].Value[0];
        bool zeroPad = flags.Contains('0');
        bool leftAlign = flags.Contains('-');

        string body = conv switch
        {
            'x' => ((long)Math.Round(value)).ToString("x", CultureInfo.InvariantCulture),
            'X' => ((long)Math.Round(value)).ToString("X", CultureInfo.InvariantCulture),
            'd' or 'i' => ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture),
            'f' => value.ToString("F" + (precision ?? 6), culture),
            'e' => value.ToString("e" + (precision ?? 6), culture),
            'E' => value.ToString("E" + (precision ?? 6), culture),
            _ => value.ToString("G" + (precision?.ToString(CultureInfo.InvariantCulture) ?? ""), culture),
        };

        if (conv is 'x' or 'X' && value < 0)
        {
            // Show negative values as two's complement of the smallest fitting width.
            long v = (long)Math.Round(value);
            body = (v >= int.MinValue ? ((uint)(int)v).ToString(conv == 'x' ? "x" : "X", CultureInfo.InvariantCulture) : body);
        }

        if (body.Length < width)
        {
            if (leftAlign)
                body = body.PadRight(width);
            else if (zeroPad && conv != 'g' && conv != 'G')
                body = body.StartsWith('-') ? "-" + body[1..].PadLeft(width - 1, '0') : body.PadLeft(width, '0');
            else
                body = body.PadLeft(width);
        }
        return body;
    }

    /// <summary>Formats a number for the clipboard (RomRaider's NumberUtil.stringValue).</summary>
    public static string ToClipboardString(double value) =>
        double.IsNaN(value) ? "NaN" : value.ToString("0.0#################", CultureInfo.CurrentCulture);

    /// <summary>
    /// Parses a user-typed number. Accepts the current culture's format, the invariant
    /// format, and hex with a 0x prefix. Spaces are ignored.
    /// </summary>
    public static bool TryParse(string? input, out double value)
    {
        value = 0;
        if (input == null)
            return false;
        var s = input.Replace(" ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (s.Length == 0)
            return false;

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            value = hex;
            return true;
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
               double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
