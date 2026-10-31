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

    [Theory]
    [InlineData("rand()")]
    [InlineData("x*rand()")]
    public void RandIsNotAScalingFunction(string expression)
    {
        Assert.Contains("Unknown function 'rand'", ExpressionEngine.Validate(expression));
        Assert.True(double.IsNaN(ExpressionEngine.Evaluate(expression, 1)));
    }

    /// <summary>A single uint8 cell holding 10, with the given scaling.</summary>
    private static DataCell CellWithScaling(string expression, string toByte)
    {
        var image = new byte[16];
        image[0] = 10;
        var rom = TestData.LoadInline(Rom("", $"""
            <table name="T" type="1D" storageaddress="0x0" storagetype="uint8">
              <scaling units="u" expression="{expression}" to_byte="{toByte}" format="0.###" fineincrement="1" coarseincrement="1"/>
            </table>
            """), image);
        return rom.GetTable("T")!.Cells[0];
    }

    /// <summary>
    /// A to_byte that gives infinity or NaN leaves the cell alone and counts the refusal; it used to
    /// clamp infinity to the storage limit (255 here) and write that.
    /// </summary>
    [Theory]
    [InlineData("1/x", "1/x", "0")]          // division by zero: +infinity
    // (An expression of exactly "x" skips to_byte, hence "x*1".)
    [InlineData("x*1", "exp(x)", "1000")]    // overflow: +infinity
    [InlineData("x*1", "-exp(x)", "1000")]   // -infinity
    [InlineData("x*1", "sqrt(-x)", "4")]     // NaN
    [InlineData("x", "x", "1e999")]          // the typed number itself overflows
    public void NonFiniteRawValueIsRejected(string expression, string toByte, string typed)
    {
        var cell = CellWithScaling(expression, toByte);
        cell.SetRealValue(typed);
        Assert.Equal(10, cell.BinValue);
        Assert.Equal(1, cell.Table.Rom!.RejectedWrites);
        Assert.False(cell.Table.Rom!.History.CanUndo);
    }

    [Fact]
    public void NonFiniteIncrementIsRejected()
    {
        // NaN (0/0) exactly at raw 11, finite everywhere else.
        var cell = CellWithScaling("x*1", "x/(x-11)*(x-11)");
        cell.Increment(1);
        Assert.Equal(10, cell.BinValue);
        Assert.Equal(1, cell.Table.Rom!.RejectedWrites);
        cell.Increment(-1);
        Assert.Equal(9, cell.BinValue);
        Assert.Equal(1, cell.Table.Rom!.RejectedWrites);
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    public void NonFiniteBinValueIsRejected(double value)
    {
        var cell = CellWithScaling("x", "x");
        cell.SetBinValue(value);
        Assert.Equal(10, cell.BinValue);
        Assert.Equal(10, cell.Table.Rom!.Binary[0]);
        Assert.Equal(1, cell.Table.Rom!.RejectedWrites);
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
        // Refused, not clamped to the storage range.
        Assert.Equal(0x1234, cell.BinValue);
        Assert.Equal(1, rom.RejectedWrites);
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
    public void MissingDefinitionIsADefinitionException()
    {
        Assert.Throws<DefinitionException>(() => DefinitionDocument.Load(Path.Combine(Path.GetTempPath(), "rl-does-not-exist.xml")));
    }

    /// <summary>
    /// XInclude is rejected, not expanded: an include could read any file or reach a \\host\share
    /// path. The included file must never be opened, even when it exists.
    /// </summary>
    [Theory]
    [InlineData("""<roms xmlns:xi="http://www.w3.org/2001/XInclude"><xi:include href="INCLUDED"/></roms>""")]
    [InlineData("""<roms><rom><x:include xmlns:x="http://www.w3.org/2001/XInclude" href="INCLUDED"/></rom></roms>""")]
    [InlineData("""<roms xmlns:xi="http://www.w3.org/2001/XInclude"><rom><table name="T"><xi:fallback/></table></rom></roms>""")]
    public void XIncludeIsRejected(string xml)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rl-inc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var included = Path.Combine(dir, "included.xml");
            File.WriteAllText(included, "<roms/>");
            var path = Path.Combine(dir, "main.xml");
            File.WriteAllText(path, xml.Replace("INCLUDED", included));
            // Held open with no sharing: if the loader tried to read it, it would fail differently.
            using var lockIncluded = new FileStream(included, FileMode.Open, FileAccess.Read, FileShare.None);

            var ex = Assert.Throws<DefinitionException>(() => DefinitionDocument.Load(path));
            Assert.Contains("XInclude", ex.Message);
            Assert.Contains("XInclude", Assert.Throws<DefinitionException>(() => DefinitionDocument.Parse(xml)).Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IncludeOutsideTheXIncludeNamespaceIsJustAnUnknownElement()
    {
        var doc = DefinitionDocument.Parse("""<roms><include href="x.xml"/><rom><romid><xmlid>A</xmlid></romid></rom></roms>""");
        Assert.Single(doc.Roms);
    }

    [Fact]
    public void DtdEntitiesAreNotExpanded()
    {
        const string xml = """
            <!DOCTYPE roms [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">]>
            <roms><rom><romid><xmlid>&b;</xmlid></romid></rom></roms>
            """;
        // Ignored DTD: the entity reference is undeclared, so the document is rejected rather than expanded.
        Assert.Throws<System.Xml.XmlException>(() => DefinitionDocument.Parse(xml));
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
