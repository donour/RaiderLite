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
            var root = LoadElement(path, depth: 0);
            return new DefinitionDocument(System.IO.Path.GetFullPath(path), root);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new DefinitionException($"{System.IO.Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    public static DefinitionDocument Parse(string xml, string path = "")
    {
        var doc = XDocument.Parse(xml);
        return new DefinitionDocument(path, doc.Root ?? throw new DefinitionException("empty document"));
    }

    private static XElement LoadElement(string path, int depth)
    {
        if (depth > 16)
            throw new DefinitionException("XInclude nesting too deep");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(path, settings);
        var doc = XDocument.Load(reader, LoadOptions.None);
        var root = doc.Root ?? throw new DefinitionException($"{path}: empty document");
        ExpandIncludes(root, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".", depth);
        return root;
    }

    /// <summary>Replaces xi:include elements (href, optional xpointer(/a/b/*)) with the referenced XML.</summary>
    private static void ExpandIncludes(XElement root, string baseDir, int depth)
    {
        foreach (var include in root.Descendants(XInclude + "include").ToList())
        {
            var href = (string?)include.Attribute("href");
            if (string.IsNullOrEmpty(href))
                continue;
            var target = System.IO.Path.Combine(baseDir, href);
            var included = LoadElement(target, depth + 1);

            var replacement = new List<XElement> { included };
            var xpointer = (string?)include.Attribute("xpointer");
            if (xpointer != null && xpointer.StartsWith("xpointer(", StringComparison.Ordinal) && xpointer.EndsWith(')'))
            {
                var steps = xpointer["xpointer(".Length..^1].Trim('/').Split('/');
                IEnumerable<XElement> current = [new XElement("doc", included)];
                foreach (var step in steps)
                {
                    current = step == "*"
                        ? current.SelectMany(e => e.Elements())
                        : current.SelectMany(e => e.Elements().Where(c => c.Name.LocalName == step));
                }
                replacement = current.ToList();
            }
            include.ReplaceWith(replacement);
        }
    }

    /// <summary>The first rom whose own internal ID matches the image (RomRaider's search order).</summary>
    public RomDefinitionInfo? FindMatch(byte[] image) => Roms.FirstOrDefault(r => r.Id.CheckMatch(image));

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
