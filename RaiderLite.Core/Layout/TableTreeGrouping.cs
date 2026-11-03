namespace RaiderLite.Core.Layout;

/// <summary>
/// Splits a large tree category into generated sub-folders using the table names alone, so a
/// category with hundreds of tables (e.g. "obd") is browsable without editing the definition.
/// <para>
/// At each level the words every name shares are skipped (they'd only make a folder holding
/// everything), then names are grouped by their next word: "obd: ii P0101 fail counter" and
/// "obd: ii P0101 pass counter" land in a "P0101" folder. A word only one name has makes no
/// folder. If that still leaves too many entries, they're bucketed by the first few characters
/// of that word ("P01…"), choosing the length that best balances the bucket count against the
/// largest bucket. Folders still over the threshold are split again the same way.
/// </para>
/// </summary>
public static class TableTreeGrouping
{
    public const int DefaultThreshold = 30;
    public const int MinThreshold = 5;

    /// <summary>Marks a bucket of names that share their first few characters.</summary>
    public const string BucketSuffix = "…";

    private const int MaxDepth = 6;

    private sealed class Entry(string key, string label, int first)
    {
        /// <summary>The word the entry is grouped by; empty for a name that is only the shared prefix.</summary>
        public string Key { get; } = key;
        public string Label { get; } = label;
        public List<int> Members { get; } = [first];
        public bool IsFolder => Members.Count > 1 && Key.Length > 0;
    }

    /// <summary>
    /// The generated folders each name goes in, outermost first, in the same order as
    /// <paramref name="names"/>; an empty path leaves the name directly in its category. Folders
    /// appear in the order their first name does, so the result follows the input's order.
    /// </summary>
    public static string[][] Group(IReadOnlyList<string> names, int threshold = DefaultThreshold)
    {
        threshold = Math.Max(MinThreshold, threshold);
        var paths = new List<string>[names.Count];
        for (int i = 0; i < paths.Length; i++)
            paths[i] = [];
        var tokens = names.Select(n => n.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToArray();
        Split([.. Enumerable.Range(0, names.Count)], 0, tokens, paths, threshold, 0);
        return [.. paths.Select(p => p.ToArray())];
    }

    private static void Split(List<int> items, int start, string[][] tokens, List<string>[] paths, int threshold, int depth)
    {
        if (items.Count <= threshold || depth >= MaxDepth)
            return;

        int p = start;
        while (items.All(i => tokens[i].Length > p && Same(tokens[i][p], tokens[items[0]][p])))
            p++;

        var entries = new List<Entry>();
        var byKey = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            if (tokens[i].Length <= p)
            {
                entries.Add(new Entry("", "", i));
                continue;
            }
            var word = tokens[i][p];
            if (byKey.TryGetValue(word, out var entry))
            {
                entry.Members.Add(i);
            }
            else
            {
                entry = new Entry(word, word, i);
                byKey[word] = entry;
                entries.Add(entry);
            }
        }

        // Single-name groups are just tables; list them one per entry.
        entries = [.. entries.SelectMany(e => e.Members.Count == 1 || e.Key.Length == 0
            ? e.Members.Select(m => new Entry(e.Key, e.Label, m))
            : [e])];

        Bucket(entries, 0, paths, threshold);

        foreach (var entry in entries.Where(e => e.IsFolder))
        {
            foreach (var m in entry.Members)
                paths[m].Add(entry.Label);
            Split(entry.Members, p + 1, tokens, paths, threshold, depth + 1);
        }
    }

    /// <summary>Groups entries by the first characters of their key when there are too many to list.</summary>
    private static void Bucket(List<Entry> entries, int prefixLength, List<string>[] paths, int threshold)
    {
        if (entries.Count <= threshold)
            return;
        var keyed = entries.Where(e => e.Key.Length > prefixLength).ToList();
        if (keyed.Count < 2)
            return;

        // Shortest prefix that gives the most even split: neither too many buckets nor one huge one.
        int bestLength = 0, bestCost = int.MaxValue;
        List<List<Entry>>? best = null;
        int longest = keyed.Max(e => e.Key.Length);
        for (int k = prefixLength + 1; k <= longest; k++)
        {
            var buckets = keyed.GroupBy(e => Prefix(e.Key, k), StringComparer.OrdinalIgnoreCase).Select(g => g.ToList()).ToList();
            int shown = entries.Count - keyed.Count + buckets.Count;
            int cost = Math.Max(shown, buckets.Max(b => b.Count));
            if (cost < bestCost)
                (bestLength, bestCost, best) = (k, cost, buckets);
        }
        if (best == null || bestCost >= entries.Count)
            return;

        foreach (var bucket in best.Where(b => b.Count > 1))
        {
            // Label with everything the bucket's words share: "freeze…" for freeze and freeze2, not "fre…".
            int shared = bucket.Skip(1).Aggregate(bucket[0].Key.Length, (n, e) => CommonPrefixLength(bucket[0].Key, e.Key, n));
            var label = bucket[0].Label[..shared] + BucketSuffix;
            foreach (var m in bucket.SelectMany(e => e.Members))
                paths[m].Add(label);
            Bucket(bucket, bestLength, paths, threshold);
        }
    }

    private static string Prefix(string key, int length) => key.Length <= length ? key : key[..length];

    private static int CommonPrefixLength(string a, string b, int max)
    {
        int n = Math.Min(max, Math.Min(a.Length, b.Length));
        int i = 0;
        while (i < n && char.ToUpperInvariant(a[i]) == char.ToUpperInvariant(b[i]))
            i++;
        return i;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
