using System.Text.Json;
using System.Xml;
using RaiderLite.Core.Model;

namespace RaiderLite.Core.Definitions;

/// <summary>
/// Refreshes definition files from the golden online location: every <c>.xml</c> file directly in
/// one folder of a GitHub repository (no subfolders, no other file types), saved into a local
/// folder and replacing earlier downloads of the same name.
/// </summary>
public sealed class DefinitionDownloader(HttpClient http)
{
    /// <summary>The golden definition location.</summary>
    public const string Owner = "donour";
    public const string Repository = "LotusECU-T4e";
    public const string Branch = "master_t6e";
    public const string FolderPath = "romraider-defs";

    /// <summary>The folder's page on GitHub (for people to look at).</summary>
    public static string WebUrl => $"https://github.com/{Owner}/{Repository}/tree/{Branch}/{FolderPath}";

    /// <summary>GitHub's contents API for the folder: one JSON entry per file and subfolder.</summary>
    public static string ListingUrl => $"https://api.github.com/repos/{Owner}/{Repository}/contents/{FolderPath}?ref={Branch}";

    /// <summary>
    /// Downloads every XML definition into <paramref name="targetDirectory"/> and returns their
    /// local paths, sorted by name. Nothing is written unless every file downloads and parses as
    /// XML, so a failed refresh leaves the earlier downloads as they were.
    /// </summary>
    public async Task<IReadOnlyList<string>> DownloadAsync(string targetDirectory, CancellationToken cancel = default)
    {
        var listing = await http.GetStringAsync(ListingUrl, cancel).ConfigureAwait(false);
        var entries = ParseListing(listing);
        if (entries.Count == 0)
            throw new InvalidDataException($"No XML definition files were found at {WebUrl}.");

        var downloaded = new List<(string Name, byte[] Bytes)>();
        foreach (var (name, url) in entries)
        {
            var bytes = await http.GetByteArrayAsync(url, cancel).ConfigureAwait(false);
            EnsureWellFormedXml(name, bytes);
            downloaded.Add((name, bytes));
        }

        Directory.CreateDirectory(targetDirectory);
        var paths = new List<string>();
        foreach (var (name, bytes) in downloaded)
        {
            var path = Path.Combine(targetDirectory, name);
            SafeFile.WriteAllBytes(path, bytes);
            paths.Add(path);
        }
        return paths;
    }

    /// <summary>
    /// The XML files in a GitHub contents listing, as (file name, download URL), sorted by name.
    /// Subfolders, other file types and names that aren't a plain file name are left out.
    /// </summary>
    public static List<(string Name, Uri Url)> ParseListing(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The definition listing isn't a folder listing.");

        var result = new List<(string Name, Uri Url)>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || Str(item, "type") != "file"
                || Str(item, "name") is not { } name
                || Str(item, "download_url") is not { } download)
                continue;
            if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || !IsPlainFileName(name))
                continue;
            if (!Uri.TryCreate(download, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                continue;
            result.Add((name, url));
        }
        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return result;
    }

    private static string? Str(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A name that can only land directly in the target folder (no paths, no "..").</summary>
    private static bool IsPlainFileName(string name) =>
        name.Length > ".xml".Length
        && name == Path.GetFileName(name)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.IndexOfAny(['/', '\\', ':']) < 0
        && !name.StartsWith('.');

    /// <summary>Rejects a download that isn't XML (an error page, a truncated file).</summary>
    private static void EnsureWellFormedXml(string name, byte[] bytes)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            while (reader.Read())
            {
            }
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"{name} isn't a valid XML file: {ex.Message}", ex);
        }
    }
}
