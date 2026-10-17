using RaiderLite.Core.Definitions;
using RaiderLite.Core.Model;

namespace RaiderLite.Tests;

/// <summary>Locations of real definitions and images used by integration tests.</summary>
internal static class TestData
{
    public static readonly string LotusRoot =
        Environment.GetEnvironmentVariable("RAIDERLITE_LOTUS_ROOT") ?? @"C:\Users\donour\source\Lotus-T4e\lotusecu-tools";

    public static readonly string RomRaiderRoot =
        Environment.GetEnvironmentVariable("RAIDERLITE_ROMRAIDER_ROOT") ?? @"C:\Users\donour\source\RomRaider";

    public static string LotusDefs => Path.Combine(LotusRoot, "romraider-defs");

    public static string T4eDef => Path.Combine(LotusDefs, "T4E090_defs.xml");

    public static string T4eBlackCalrom => Path.Combine(LotusRoot, @"dump\t4e-black\A129E0002\calrom.bin");

    public static string EmiraDef => Path.Combine(LotusDefs, "8900689277A-emira.xml");

    public static string EmiraCal => Path.Combine(LotusRoot, @"disassembly\emira\8900689277A\emira_cal.bin");

    public static string T6Def => Path.Combine(LotusDefs, "T6-V00BU_defs.xml");

    public static string T6Calrom => Path.Combine(LotusRoot, @"custom_firmware\build\s1-race-tc\calrom.bin");

    /// <summary>Lotus .cpt calibration (raw region contents, no header).</summary>
    public static string T6CalCpt => Path.Combine(LotusRoot, @"custom_firmware\bases\B132E0091_TAB.cpt");

    public static string ScalingBaseTestDef => Path.Combine(RomRaiderRoot, @"src\test\definitions\scalingbase_test.xml");

    /// <summary>Loads an image with the definition's first matching rom.</summary>
    public static Rom LoadMatching(string defPath, string imagePath)
    {
        var doc = DefinitionDocument.Load(defPath);
        var image = File.ReadAllBytes(imagePath);
        var match = doc.FindMatch(image) ?? throw new InvalidOperationException($"No rom in {defPath} matches {imagePath}");
        return RomUnmarshaller.Load(doc, match.Node, image, imagePath);
    }

    /// <summary>Builds a rom from inline definition XML and an image.</summary>
    public static Rom LoadInline(string xml, byte[] image, string? xmlId = null)
    {
        var doc = DefinitionDocument.Parse(xml);
        var info = xmlId != null ? doc.FindByXmlId(xmlId)! : doc.Roms[0];
        return RomUnmarshaller.Load(doc, info.Node, image);
    }
}

/// <summary>A fact that is skipped when the listed files don't exist on this machine.</summary>
internal sealed class RequiresFilesFactAttribute : FactAttribute
{
    public RequiresFilesFactAttribute(params string[] keys)
    {
        foreach (var key in keys)
        {
            var path = typeof(TestData).GetProperty(key)?.GetValue(null) as string ?? key;
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Skip = $"Test data not found: {path}";
                return;
            }
        }
    }
}
