using System.Globalization;
using RaiderLite.Core.Definitions;
using RaiderLite.Core.Expressions;
using RaiderLite.Core.Model;

namespace RaiderLite.Tests;

/// <summary>Malformed definitions and inputs must produce warnings or errors, never crashes.</summary>
public class RobustnessTests
{
    public RobustnessTests()
    {
        CoreSettings.Current = new CoreSettings();
    }

    private static string Rom(string romId, string tables) => $"""
        <roms><rom>
          <romid><xmlid>R</xmlid><noRamOffset/>{romId}</romid>
          {tables}
        </rom></roms>
        """;

    [Fact]
    public void EmptyOrInvalidRomIdFieldsDontThrow()
    {
        var xml = Rom("<internalidaddress></internalidaddress><internalidstring>ABC</internalidstring><offset>zz</offset><filesize>huge</filesize>",
            """<table name="T" type="1D" storageaddress="0x0" storagetype="uint8"/>""");
        var doc = DefinitionDocument.Parse(xml);
        Assert.Null(doc.FindMatch(new byte[16]));
        var rom = RomUnmarshaller.Load(doc, doc.Roms[0].Node, new byte[16]);
        Assert.NotNull(rom.GetTable("T"));
        Assert.Contains(rom.Warnings, w => w.Contains("offset", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("""<table name="Zero" type="1D" storageaddress="0x0" storagetype="uint8" sizex="0"/>""", "Zero")]
    [InlineData("""<table name="Negative" type="1D" storageaddress="0x0" storagetype="uint8" sizex="-5"/>""", "Negative")]
    [InlineData("""<table name="Map0" type="3D" storageaddress="0x0" storagetype="uint8" sizex="0" sizey="4"><table type="X Axis" name="x" storageaddress="0x8"/><table type="Y Axis" name="y" storageaddress="0x9"/></table>""", "Map0")]
    [InlineData("""<table name="NoBits" type="BitwiseSwitch" storageaddress="0x0" storagetype="uint8"/>""", "NoBits")]
    public void TablesWithNoValuesAreRemoved(string table, string name)
    {
        var rom = TestData.LoadInline(Rom("", table), new byte[64]);
        Assert.Null(rom.GetTable(name));
        Assert.Contains(name, rom.FaultyTables);
    }

    [Fact]
    public void AxisBasedOnAMapIsIgnoredWithAWarning()
    {
        var rom = TestData.LoadInline(Rom("", """
            <table name="M" type="3D" storageaddress="0x0" storagetype="uint8" sizex="2" sizey="2">
              <table type="X Axis" name="mx" storageaddress="0x10" storagetype="uint8"/><table type="Y Axis" name="my" storageaddress="0x12" storagetype="uint8"/>
            </table>
            <table name="C" type="2D" storageaddress="0x20" storagetype="uint8" sizex="2">
              <table type="X Axis" name="cx" base="M" storageaddress="0x30" storagetype="uint8"/>
            </table>
            """), new byte[64]);
        Assert.IsType<Table1D>(((Table2D)rom.GetTable("C")!).Axis);
        Assert.Contains(rom.Warnings, w => w.Contains("not a 1D table", StringComparison.Ordinal));
    }

    [Fact]
    public void BitPositionOutOfRangeIsIgnored()
    {
        var rom = TestData.LoadInline(Rom("", """
            <table name="Flags" type="BitwiseSwitch" storageaddress="0x0" storagetype="uint8">
              <bit name="ok" position="1"/>
              <bit name="too high" position="12"/>
              <bit name="negative" position="-1"/>
            </table>
            """), new byte[4]);
        var t = (TableBitwiseSwitch)rom.GetTable("Flags")!;
        Assert.Equal([1], t.Bits);
        Assert.Single(t.Presets!.Presets);
    }

    [Fact]
    public void DeeplyNestedExpressionIsRejectedNotStackOverflow()
    {
        var expr = new string('(', 5000) + "x" + new string(')', 5000);
        Assert.NotNull(ExpressionEngine.Validate(expr));
        Assert.True(double.IsNaN(ExpressionEngine.Evaluate(expr, 1)));
        var chain = string.Join("+", Enumerable.Repeat("x", 50_000));
        Assert.NotNull(ExpressionEngine.Validate(chain));
    }

    [Fact]
    public void ChecksumOutsideImageDoesNotThrow()
    {
        var xml = """
            <roms><rom>
              <romid><xmlid>R</xmlid><noRamOffset/></romid>
              <checksum type="std" start="0x0" end="0x100000" sumloc="0x10" xorloc="0x14"/>
              <checksum type="bytexor" start="0x0" end="0x10" xorloc="0x5000"/>
              <table name="T" type="1D" storageaddress="0x0" storagetype="uint8"/>
            </rom></roms>
            """;
        var rom = TestData.LoadInline(xml, new byte[64]);
        Assert.Equal(0, rom.ValidateChecksums());
        var bytes = rom.PrepareForSave(out int updated);
        Assert.Equal(0, updated);
        Assert.Equal(2, rom.ChecksumErrors.Count);
        Assert.Equal(64, bytes.Length);
    }

    [Theory]
    [InlineData("%99999999999d")]
    [InlineData("%.99999999999f")]
    [InlineData("%999d")]
    [InlineData("#,##0.00;(#,##0.00)")]
    [InlineData("\\")]
    public void OddFormatsDontThrow(string format)
    {
        var text = ValueFormatter.Format(12.5, format, CultureInfo.InvariantCulture);
        Assert.False(string.IsNullOrEmpty(text));
        Assert.True(text.Length < 200);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]
    public void NonFiniteInputNeverWritesGarbage(string input)
    {
        var rom = TestData.LoadInline(Rom("", """<table name="T" type="1D" storageaddress="0x0" storagetype="uint16"><scaling units="x" expression="x" to_byte="x" format="0"/></table>"""),
            [0x12, 0x34, 0, 0]);
        var cell = rom.GetTable("T")!.Cells[0];
        cell.SetRealValue(input);
        if (input.Contains("NaN", StringComparison.Ordinal))
        {
            Assert.Equal(0x1234, cell.BinValue);
        }
        else
        {
            // Infinities clamp to the storage range.
            Assert.True(cell.BinValue is 0 or 65535);
        }
        cell.SetBinValue(double.NaN);
        Assert.False(double.IsNaN(cell.BinValue));
    }

    [Fact]
    public void MalformedXmlIsADefinitionException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rl-bad-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, "<roms><rom><romid>");
        try
        {
            Assert.Throws<DefinitionException>(() => DefinitionDocument.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingDefinitionOrIncludeIsADefinitionException()
    {
        Assert.Throws<DefinitionException>(() => DefinitionDocument.Load(Path.Combine(Path.GetTempPath(), "rl-does-not-exist.xml")));

        var path = Path.Combine(Path.GetTempPath(), $"rl-inc-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, """<roms xmlns:xi="http://www.w3.org/2001/XInclude"><xi:include href="missing.xml"/></roms>""");
        try
        {
            Assert.Throws<DefinitionException>(() => DefinitionDocument.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UndoHistoryIsBounded()
    {
        var rom = TestData.LoadInline(Rom("", """<table name="T" type="1D" storageaddress="0x0" storagetype="uint16"/>"""), new byte[4]);
        var cell = rom.GetTable("T")!.Cells[0];
        for (int i = 1; i <= EditHistory.MaxSteps + 500; i++)
            cell.SetBinValue(i);
        int undone = 0;
        while (rom.History.CanUndo)
        {
            rom.History.Undo();
            undone++;
        }
        Assert.Equal(EditHistory.MaxSteps, undone);
    }
}
