using System.Globalization;
using System.Text;

namespace RaiderLite.Core.Tunes;

/// <summary>
/// Revision notes in the "* table: what changed" bullet style: suggested lines for changed tables,
/// a loose check of whether notes mention a table, and the cumulative changelog of a lineage.
/// </summary>
public static class Changelog
{
    /// <summary>"inj: afr base" -> "* inj afr base: ", the bullet a note starts from.</summary>
    public static string NoteLine(string tableName) => $"* {SpokenName(tableName)}: ";

    /// <summary>Lines for the tables not yet mentioned in <paramref name="notes"/>, one per table.</summary>
    public static string SuggestedLines(string notes, IEnumerable<string> tableNames) =>
        string.Concat(tableNames.Where(t => !Mentions(notes, t)).Distinct().Select(t => NoteLine(t) + Environment.NewLine));

    /// <summary>
    /// Whether some line of <paramref name="notes"/> names the table, loosely: every word of its name
    /// (or of the part after the category, "afr base" of "inj: afr base") starts a word of the line,
    /// ignoring case and punctuation, and words of two letters ("ii" of "obd: ii P0139") when longer
    /// ones remain. "sensor_maf_scaling", "maf scaling" and "CAL_obd_P0171/2_..." count; a
    /// different table doesn't.
    /// </summary>
    public static bool Mentions(string notes, string tableName)
    {
        var full = Significant(Words(tableName));
        int colon = tableName.IndexOf(':');
        var shortName = colon >= 0 ? Significant(Words(tableName[(colon + 1)..])) : full;
        if (full.Count == 0)
            return true;
        foreach (var line in notes.Split('\n'))
        {
            var words = Words(line);
            bool Has(IReadOnlyList<string> name) => name.Count > 0 && name.All(n => words.Any(w => w.StartsWith(n, StringComparison.Ordinal)));
            if (Has(full) || Has(shortName))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The notes of <paramref name="revision"/> and every revision it is based on, newest first, each
    /// under a "yyyyMMdd - 012 Label" heading: the format of a cumulative notes.txt.
    /// </summary>
    public static string Build(Car car, Revision revision, Func<Revision, string> readNotes)
    {
        var sb = new StringBuilder();
        foreach (var r in car.Lineage(revision))
        {
            var heading = $"{r.Created.ToString("yyyyMMdd", CultureInfo.InvariantCulture)} - {r.Number:000}";
            if (r.Label.Length > 0)
                heading += " " + r.Label;
            sb.AppendLine(heading);
            sb.AppendLine();
            var notes = readNotes(r).Trim();
            if (notes.Length > 0)
            {
                sb.AppendLine(notes.ReplaceLineEndings(Environment.NewLine));
                sb.AppendLine();
            }
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string SpokenName(string tableName) => string.Join(' ', tableName.Split([' ', ':'], StringSplitOptions.RemoveEmptyEntries));

    private static List<string> Significant(List<string> words) =>
        words.Any(w => w.Length > 2) ? words.Where(w => w.Length > 2).ToList() : words;

    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (sb.Length > 0)
            {
                words.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length > 0)
            words.Add(sb.ToString());
        return words;
    }
}
