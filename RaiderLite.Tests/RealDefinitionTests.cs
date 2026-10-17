using RaiderLite.Core.Definitions;
using RaiderLite.Core.Model;
using Xunit.Abstractions;

namespace RaiderLite.Tests;

/// <summary>Integration tests against the Lotus definitions and dumps.</summary>
public class RealDefinitionTests(ITestOutputHelper output)
{
    [RequiresFilesFact(nameof(TestData.LotusDefs))]
    public void AllLotusDefinitionsBuildEveryRom()
    {
        CoreSettings.Current = new CoreSettings();
        foreach (var file in Directory.GetFiles(TestData.LotusDefs, "*.xml"))
        {
            var doc = DefinitionDocument.Load(file);
            Assert.NotEmpty(doc.Roms);
            foreach (var info in doc.Roms)
            {
                var rom = new RomUnmarshaller(doc).Unmarshal(info.Node);
                output.WriteLine($"{Path.GetFileName(file)} / {info.Id.XmlId}: {rom.Tables.Count} tables, {rom.Warnings.Count} warnings");
                foreach (var w in rom.Warnings.Take(5))
                    output.WriteLine("    " + w);
                Assert.NotEmpty(rom.Tables);
                Assert.DoesNotContain(rom.Warnings, w => w.Contains("Table type", StringComparison.Ordinal));
            }
        }
    }

    [RequiresFilesFact(nameof(TestData.T4eDef), nameof(TestData.T4eBlackCalrom))]
    public void T4eCalromMatchesAndPopulates()
    {
        CoreSettings.Current = new CoreSettings();
        var rom = TestData.LoadMatching(TestData.T4eDef, TestData.T4eBlackCalrom);
        Assert.Equal("T4E090", rom.RomId.XmlId);
        Assert.Empty(rom.FaultyTables);
        Assert.Equal(0, rom.RomId.RamOffset);
        output.WriteLine($"{rom.Tables.Count} tables"); foreach (var w in rom.Warnings) output.WriteLine("W: " + w);

        var image = File.ReadAllBytes(TestData.T4eBlackCalrom);

        // 3D: 8x8 uint8 at 0x1454, axes at 0x1444 / 0x144C, expression x*5.
        var shutdown = (Table3D)rom.GetTable("ecu: shutdown delay")!;
        Assert.Equal(8, shutdown.Data3D.GetLength(0));
        Assert.Equal(8, shutdown.Data3D.GetLength(1));
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
                Assert.Equal(image[0x1454 + y * 8 + x] * 5.0, shutdown.Data3D[x, y].RealValue, 6);
        }
        Assert.Equal(image[0x1444] * 5.0 / 8 - 40, shutdown.XAxis.Cells[0].RealValue, 6);
        Assert.Equal(image[0x144C] * 5.0 / 8 - 40, shutdown.YAxis.Cells[0].RealValue, 6);

        // Scalar stored as a 2D table with a one-entry static axis.
        var startRelay = (Table2D)rom.GetTable("ecu: startrelay runtime max")!;
        Assert.True(startRelay.Axis.IsStaticDataTable);
        Assert.Equal(((image[0x120] << 8) | image[0x121]) * 5.0 / 1000, startRelay.Cells[0].RealValue, 6);

        // Lotus scalars are declared "2D" with one static axis entry: they count as scalars.
        Assert.Equal(TableShape.Scalar, startRelay.Shape);
        Assert.Equal(TableShape.Map, shutdown.Shape);

        // Switch with 4-byte states.
        var unlock = (TableSwitch)rom.GetTable("ecu: unlock magic")!;
        Assert.Equal(4, unlock.Cells.Length);
        Assert.Equal(3, unlock.Presets!.Presets.Count);
    }

    [RequiresFilesFact(nameof(TestData.T4eDef))]
    public void T4eChildRomsOverrideBaseAddresses()
    {
        var doc = DefinitionDocument.Load(TestData.T4eDef);
        var child = doc.FindByXmlId("D120E0030H")!;
        var rom = new RomUnmarshaller(doc).Unmarshal(child.Node);
        Assert.Equal("D120E0030H", rom.RomId.XmlId);
        Assert.Equal(15508, rom.RomId.FileSize);
        Assert.Equal(0x3C8E, rom.GetTable("ecu: unlock magic")!.StorageAddress);

        var baseRom = new RomUnmarshaller(doc).Unmarshal(doc.FindByXmlId("T4E090")!.Node);
        Assert.Equal(0x3CAE, baseRom.GetTable("ecu: unlock magic")!.StorageAddress);
        Assert.Equal(baseRom.Tables.Count, rom.Tables.Count);
    }

    [RequiresFilesFact(nameof(TestData.EmiraDef), nameof(TestData.EmiraCal))]
    public void EmiraCalibrationLoads()
    {
        CoreSettings.Current = new CoreSettings();
        var rom = TestData.LoadMatching(TestData.EmiraDef, TestData.EmiraCal);
        Assert.Empty(rom.FaultyTables);
        Assert.Contains(rom.Tables, t => t is Table3D);
        Assert.Contains(rom.Tables, t => t is TableSwitch);
    }

    [RequiresFilesFact(nameof(TestData.T6Def), nameof(TestData.T6Calrom))]
    public void T6CalromLoads()
    {
        CoreSettings.Current = new CoreSettings();
        var rom = TestData.LoadMatching(TestData.T6Def, TestData.T6Calrom);
        output.WriteLine($"{rom.RomId.XmlId}: {rom.Tables.Count} tables, faulty: {string.Join(", ", rom.FaultyTables)}");
        Assert.Empty(rom.FaultyTables);
    }

    [RequiresFilesFact(nameof(TestData.T6Def), nameof(TestData.T6CalCpt))]
    public void LotusCptImageLoadsAndSavesUnchanged()
    {
        CoreSettings.Current = new CoreSettings();
        var original = File.ReadAllBytes(TestData.T6CalCpt);
        var rom = TestData.LoadMatching(TestData.T6Def, TestData.T6CalCpt);
        output.WriteLine($"{rom.RomId.XmlId}: {rom.Tables.Count} tables, {original.Length} bytes");
        Assert.Empty(rom.FaultyTables);
        Assert.Equal(original, rom.PrepareForSave(out _));
    }

    [RequiresFilesFact(nameof(TestData.T4eDef), nameof(TestData.T4eBlackCalrom))]
    public void SaveWithoutEditsIsByteIdentical()
    {
        CoreSettings.Current = new CoreSettings();
        var original = File.ReadAllBytes(TestData.T4eBlackCalrom);
        var rom = TestData.LoadMatching(TestData.T4eDef, TestData.T4eBlackCalrom);
        var saved = rom.PrepareForSave(out _);
        Assert.Equal(original, saved);
    }

    [RequiresFilesFact(nameof(TestData.T4eDef), nameof(TestData.T4eBlackCalrom))]
    public void EveryCellRoundTripsThroughItsScaling()
    {
        // Writing each cell's displayed real value back must not change the image.
        CoreSettings.Current = new CoreSettings { UserLevel = 5 };
        var original = File.ReadAllBytes(TestData.T4eBlackCalrom);
        var rom = TestData.LoadMatching(TestData.T4eDef, TestData.T4eBlackCalrom);
        var changed = new List<string>();
        foreach (var table in rom.Tables)
        {
            foreach (var cell in table.AllCells.Where(c => !c.IsStatic))
            {
                double before = cell.BinValue;
                cell.SetRealValue(cell.RealValue);
                if (cell.BinValue != before)
                    changed.Add($"{table.Name}[{cell.Index}] {before} -> {cell.BinValue} ({cell.Table.CurrentScale?.Expression} / {cell.Table.CurrentScale?.ByteExpression})");
            }
        }
        foreach (var c in changed.Take(20))
            output.WriteLine(c);
        Assert.Empty(changed);
        Assert.Equal(original, rom.Binary);
    }

    [RequiresFilesFact(nameof(TestData.T4eDef), nameof(TestData.T4eBlackCalrom))]
    public void EditChangesOnlyTheExpectedBytes()
    {
        CoreSettings.Current = new CoreSettings();
        var original = File.ReadAllBytes(TestData.T4eBlackCalrom);
        var rom = TestData.LoadMatching(TestData.T4eDef, TestData.T4eBlackCalrom);
        var shutdown = (Table3D)rom.GetTable("ecu: shutdown delay")!;
        var cell = shutdown.Data3D[2, 3];
        cell.SetRealValue(cell.RealValue + 25);

        var diff = Enumerable.Range(0, original.Length).Where(i => original[i] != rom.Binary[i]).ToList();
        Assert.Equal([0x1454 + 3 * 8 + 2], diff);
        Assert.Equal(original[0x1454 + 3 * 8 + 2] + 5, rom.Binary[0x1454 + 3 * 8 + 2]);
    }

    [RequiresFilesFact(nameof(TestData.ScalingBaseTestDef))]
    public void RomRaiderScalingBaseTestDefinition()
    {
        CoreSettings.Current = new CoreSettings();
        var doc = DefinitionDocument.Load(TestData.ScalingBaseTestDef);
        var child = doc.FindByXmlId("A2WC522S")!;
        var rom = new RomUnmarshaller(doc).Unmarshal(child.Node);

        var a = rom.GetTable("Target Boost A")!;
        var b = rom.GetTable("Target Boost B")!;
        Assert.NotSame(a, b);
        Assert.Equal("Inherited Target Boost A", b.Description);
        Assert.Contains(b.Scales, s => s.Category == "Metric" && s.Expression == "x*.001333224");
        Assert.Contains(b.Scales, s => s.Category == "Standard");
        // Metric is the default preferred scale.
        Assert.Equal("Metric", a.CurrentScale!.Category);
        var yAxis = ((Table3D)a).YAxis;
        Assert.Equal("RPM", yAxis.Scales.Single(s => s.Name == "rpm").Unit);
        Assert.Equal(StorageType.Float, yAxis.StorageType);
    }
}
