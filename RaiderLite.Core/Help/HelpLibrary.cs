using System.Reflection;

namespace RaiderLite.Core.Help;

/// <summary>Where a help link leads: a page of this help (and maybe a heading on it), or a web address.</summary>
public sealed record HelpTarget(HelpTopic? Topic, string? Anchor, string? Url);

/// <summary>
/// The help pages, in contents order. Pages are Markdown files named "NN-id.md"; the number
/// orders the contents and the rest is the page's id in links.
/// </summary>
public sealed class HelpLibrary
{
    private readonly Dictionary<string, HelpTopic> _byId;

    public HelpLibrary(IEnumerable<HelpTopic> topics)
    {
        Topics = topics.ToList();
        _byId = new Dictionary<string, HelpTopic>(StringComparer.OrdinalIgnoreCase);
        foreach (var topic in Topics)
        {
            if (!_byId.TryAdd(topic.Id, topic))
                throw new ArgumentException($"Two help pages are named {topic.Id}.", nameof(topics));
        }
    }

    public IReadOnlyList<HelpTopic> Topics { get; }

    public HelpTopic? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The page id in a file name: "03-editing.md" → "editing".</summary>
    public static string IdFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        int dash = name.IndexOf('-');
        return dash > 0 && name[..dash].All(char.IsAsciiDigit) ? name[(dash + 1)..] : name;
    }

    /// <summary>Parses pages given as (file name, Markdown), ordered by file name.</summary>
    public static HelpLibrary Parse(IEnumerable<(string FileName, string Markdown)> pages) =>
        new(pages.OrderBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
                 .Select(p => HelpTopic.Parse(IdFromFileName(p.FileName), p.Markdown)));

    /// <summary>Loads the pages embedded in <paramref name="assembly"/> under resource names "Help/NN-id.md".</summary>
    public static HelpLibrary FromAssembly(Assembly assembly)
    {
        var pages = new List<(string, string)>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Help/", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            pages.Add((name["Help/".Length..], reader.ReadToEnd()));
        }
        return Parse(pages);
    }

    /// <summary>
    /// Resolves a link on <paramref name="from"/>: "#anchor" (on the same page), "id" or
    /// "id#anchor" (another page), or an http(s) address. Null if it leads nowhere.
    /// </summary>
    public HelpTarget? Resolve(string link, HelpTopic? from)
    {
        if (link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || link.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return new HelpTarget(null, null, link);
        int hash = link.IndexOf('#');
        var id = hash < 0 ? link : link[..hash];
        var anchor = hash < 0 ? null : link[(hash + 1)..];
        var topic = id.Length == 0 ? from : Find(id);
        if (topic == null || (anchor != null && topic.FindHeading(anchor) == null))
            return null;
        return new HelpTarget(topic, anchor, null);
    }

    /// <summary>The search words in <paramref name="query"/>: split at spaces, at least one character each.</summary>
    public static string[] SearchTerms(string query) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The pages containing every word of <paramref name="query"/> (ignoring case), those with
    /// every word in the title first, then by how often the words occur.
    /// </summary>
    public IReadOnlyList<HelpTopic> Search(string query)
    {
        var terms = SearchTerms(query);
        if (terms.Length == 0)
            return Topics;
        return Topics
            .Where(t => terms.All(term => t.Title.Contains(term, StringComparison.OrdinalIgnoreCase) || t.PlainText.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select((t, index) => (Topic: t, index,
                InTitle: terms.All(term => t.Title.Contains(term, StringComparison.OrdinalIgnoreCase)),
                Hits: terms.Sum(term => Occurrences(t.PlainText, term))))
            .OrderByDescending(x => x.InTitle)
            .ThenByDescending(x => x.Hits)
            .ThenBy(x => x.index)
            .Select(x => x.Topic)
            .ToList();
    }

    private static int Occurrences(string text, string term)
    {
        int count = 0;
        for (int i = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(term, i + term.Length, StringComparison.OrdinalIgnoreCase))
            count++;
        return count;
    }
}
