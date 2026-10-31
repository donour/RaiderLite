using System.Xml;
using System.Xml.Linq;
using RaiderLite.Core.Model;

namespace RaiderLite.Core.Definitions;

public sealed class DefinitionException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A &lt;rom&gt; element and its own (not inherited) identification.</summary>
public sealed record RomDefinitionInfo(XElement Node, RomId Id)
{
    public string? Base => Xml.Attr(Node, "base") is { } b && !b.Equals("none", StringComparison.OrdinalIgnoreCase) ? b : null;

    public override string ToString() => Id.ToString();
}

/// <summary>A loaded ECU definition file (&lt;roms&gt; root).</summary>
public sealed class DefinitionDocument
{
    private static readonly XNamespace XInclude = "http://www.w3.org/2001/XInclude";

    private DefinitionDocument(string path, XElement root)
    {
        Path = path;
        Root = root;
        Roms = root.Elements().Where(e => Xml.Is(e, "rom"))
            .Select(e => new RomDefinitionInfo(e, ParseOwnRomId(e)))
            .ToList();
    }

    public string Path { get; }

    public XElement Root { get; }

    /// <summary>The &lt;rom&gt; elements in file order.</summary>
    public IReadOnlyList<RomDefinitionInfo> Roms { get; }

    public static DefinitionDocument Load(string path)
    {
        try
        {
            using var reader = XmlReader.Create(path, ReaderSettings);
            return FromDocument(XDocument.Load(reader, LoadOptions.None), System.IO.Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new DefinitionException($"{System.IO.Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    public static DefinitionDocument Parse(string xml, string path = "")
    {
        using var reader = XmlReader.Create(new StringReader(xml), ReaderSettings);
        return FromDocument(XDocument.Load(reader, LoadOptions.None), path);
    }

    /// <summary>
    /// No DTDs (so no entity expansion) and nothing fetched from outside the file.
    /// </summary>
    private static readonly XmlReaderSettings ReaderSettings = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

    private static DefinitionDocument FromDocument(XDocument doc, string path)
    {
        var root = doc.Root ?? throw new DefinitionException($"{System.IO.Path.GetFileName(path)}: empty document");
        // RomRaider expands xi:include; RaiderLite deliberately doesn't. An include can name any
        // file, including \\host\share paths that make Windows connect to another computer and
        // send it the user's login hash. Fail loudly rather than load a definition with parts
        // silently missing.
        if (root.DescendantsAndSelf().Any(e => e.Name.Namespace == XInclude))
            throw new DefinitionException(
                $"{System.IO.Path.GetFileName(path)}: XInclude (xi:include) isn't supported. Merge the included files into one definition file.");
        return new DefinitionDocument(path, root);
    }

    /// <summary>Every rom whose own internal ID matches the image, most specific (longest ID) first,
    /// in file order otherwise.</summary>
    public IEnumerable<RomDefinitionInfo> FindMatches(byte[] image) =>
        Roms.Where(r => r.Id.CheckMatch(image)).OrderByDescending(r => r.Id.MatchLength);

    /// <summary>The most specific rom whose internal ID matches the image; RomRaider takes the first
    /// in file order, which picks the wrong one when one ID is a prefix of another.</summary>
    public RomDefinitionInfo? FindMatch(byte[] image) => FindMatches(image).FirstOrDefault();

    public RomDefinitionInfo? FindByXmlId(string xmlId) =>
        Roms.FirstOrDefault(r => r.Id.XmlId.Equals(xmlId, StringComparison.OrdinalIgnoreCase));

    private static RomId ParseOwnRomId(XElement romNode)
    {
        var id = new RomId();
        var romIdNode = romNode.Elements().FirstOrDefault(e => Xml.Is(e, "romid"));
        if (romIdNode != null)
            RomUnmarshaller.ParseRomId(romIdNode, id, _ => { });
        return id;
    }

    public override string ToString() => System.IO.Path.GetFileName(Path);
}

/// <summary>Case-insensitive XML helpers matching RomRaider's DOMHelper.</summary>
internal static class Xml
{
    public static bool Is(XElement e, string name) => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Attribute value, or null if missing or "NaN".</summary>
    public static string? Attr(XElement e, string name)
    {
        foreach (var a in e.Attributes())
        {
            if (string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                return a.Value == "NaN" ? null : a.Value;
        }
        return null;
    }

    public static bool Bool(string? s, bool fallback) => s == null ? fallback : s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    public static int Int(string? s, int fallback) =>
        s == null ? fallback : int.Parse(s.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);

    public static double Double(string s) =>
        double.Parse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Concatenated direct text children (RomRaider's unmarshallText).</summary>
    public static string Text(XElement e) => string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value));
}
