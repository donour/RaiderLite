using RaiderLite.Core.Definitions;
using RaiderLite.Core.Model;

namespace RaiderLite.Tests;

/// <summary>Helpers for building roms from inline definitions in tests.</summary>
internal static class TestData
{
    /// <summary>Builds a rom from inline definition XML and an image.</summary>
    public static Rom LoadInline(string xml, byte[] image, string? xmlId = null)
    {
        var doc = DefinitionDocument.Parse(xml);
        var info = xmlId != null ? doc.FindByXmlId(xmlId)! : doc.Roms[0];
        return RomUnmarshaller.Load(doc, info.Node, image);
    }
}
