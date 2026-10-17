using RaiderLite.Core.Definitions;
using RaiderLite.Core.Model;

namespace RaiderLite.Tests;

public class SyntheticDefinitionTests
{
    internal const string Xml = """
        <roms>
          <scalingbase name="rpmbase" units="RPM" expression="x*25" to_byte="x/25" format="0" fineincrement="25" coarseincrement="100"/>
          <rom>
            <romid>
              <xmlid>BASE</xmlid>
              <internalidaddress>0x0</internalidaddress>
              <internalidstring>TESTROM1</internalidstring>
              <filesize>256b</filesize>
              <make>Acme</make>
            </romid>
            <table name="Scalar" category="Cat // Sub" type="1D" storageaddress="0x10" storagetype="uint16" endian="big" userlevel="1">
              <scaling category="Metric" units="C" expression="x/10" to_byte="x*10" format="0.0" fineincrement="0.1" coarseincrement="1"/>
              <scaling category="Standard" units="F" expression="(x/10)*1.8+32" to_byte="((x-32)/1.8)*10" format="0.0" fineincrement="0.2" coarseincrement="2"/>
              <description>A scalar.</description>
            </table>
            <table name="Curve" type="2D" storageaddress="0x20" storagetype="uint8" sizex="4">
              <scaling units="%" expression="x/2" to_byte="x*2" format="0.0" fineincrement="0.5" coarseincrement="5"/>
              <table name="RPM" type="X Axis" storageaddress="0x30" storagetype="uint8"><scaling base="rpmbase"/></table>
            </table>
            <table name="StaticCurve" type="2D" storageaddress="0x40" storagetype="int8" sizex="3">
              <scaling units="deg" expression="x" to_byte="x" format="0"/>
              <table name="gear" type="Static X Axis" sizex="3"><data>1st</data><data>2nd</data><data>3rd</data></table>
            </table>
            <table name="Map" type="3D" storageaddress="0x50" storagetype="uint8" sizex="3" sizey="2">
              <scaling units="ms" expression="x" to_byte="x" format="0"/>
              <table name="X" type="X Axis" storageaddress="0x60" storagetype="uint8"/>
              <table name="Y" type="Y Axis" storageaddress="0x68" storagetype="uint8"/>
            </table>
            <table name="SwappedMap" type="3D" storageaddress="0x50" storagetype="uint8" sizex="3" sizey="2" swapxy="true">
              <table name="X" type="X Axis" storageaddress="0x60" storagetype="uint8"/>
              <table name="Y" type="Y Axis" storageaddress="0x68" storagetype="uint8"/>
            </table>
            <table name="Mode" type="Switch" storageaddress="0x70" sizey="2">
              <state name="Off" data="00 00"/>
              <state name="On" data="12 34"/>
            </table>
            <table name="Flags" type="BitwiseSwitch" storageaddress="0x72" storagetype="uint8">
              <bit name="A" position="0"/>
              <bit name="B" position="3"/>
            </table>
            <table name="Masked" type="1D" storageaddress="0x74" storagetype="uint8" mask="0xF0"/>
            <table name="NoAddress" type="1D" storagetype="uint8"/>
            <table name="Level5" type="1D" storageaddress="0x75" storagetype="uint8" userlevel="5"/>
            <table name="FloatVal" type="1D" storageaddress="0x78" storagetype="float"/>
            <table name="Signed16LE" type="1D" storageaddress="0x7C" storagetype="int16" endian="little"/>
            <table name="Alias" type="1D" storageaddress="0x10" storagetype="uint16"/>
            <table name="OutOfRange" type="1D" storageaddress="0x1000" storagetype="uint8"/>
          </rom>
          <rom base="BASE">
            <romid>
              <xmlid>CHILD</xmlid>
              <internalidaddress>0x0</internalidaddress>
              <internalidstring>TESTROM2</internalidstring>
            </romid>
            <table name="Scalar" storageaddress="0x12"/>
            <table name="Masked" omit="true"/>
            <table name="Copied" base="Curve" storageaddress="0x80"/>
            <table name="NoAddress" storageaddress="0x90"/>
          </rom>
        </roms>
        """;

    internal static byte[] MakeImage(string id = "TESTROM1")
    {
        var image = new byte[256];
        System.Text.Encoding.ASCII.GetBytes(id).CopyTo(image, 0);
        image[0x10] = 0x01; image[0x11] = 0xF4;          // Scalar = 500 -> 50.0 C
        image[0x12] = 0x00; image[0x13] = 0x64;          // child's Scalar = 100
        for (int i = 0; i < 4; i++)
        {
            image[0x20 + i] = (byte)(10 * (i + 1));      // Curve data 10,20,30,40 -> 5..20 %
            image[0x30 + i] = (byte)(40 * (i + 1));      // RPM axis 40.. -> 1000.. rpm
        }
        image[0x40] = 0xFE; image[0x41] = 0; image[0x42] = 5;
        for (int i = 0; i < 6; i++)
            image[0x50 + i] = (byte)i;
        image[0x60] = 1; image[0x61] = 2; image[0x62] = 3;
        image[0x68] = 10; image[0x69] = 20;
        image[0x70] = 0x12; image[0x71] = 0x34;
        image[0x72] = 0b0000_1000;
        image[0x74] = 0xA5;
        image[0x75] = 7;
        new byte[] { 0x40, 0x49, 0x0F, 0xDB }.CopyTo(image, 0x78); // pi, big endian
        image[0x7C] = 0xFE; image[0x7D] = 0xFF;          // -2 little endian
        return image;
    }

    private static Rom LoadBase() => TestData.LoadInline(Xml, MakeImage());

    public SyntheticDefinitionTests()
    {
        CoreSettings.Current = new CoreSettings();
    }

    [Fact]
    public void FindsMatchingRomByInternalId()
    {
        var doc = DefinitionDocument.Parse(Xml);
        Assert.Equal("BASE", doc.FindMatch(MakeImage("TESTROM1"))!.Id.XmlId);
        Assert.Equal("CHILD", doc.FindMatch(MakeImage("TESTROM2"))!.Id.XmlId);
        Assert.Null(doc.FindMatch(MakeImage("NOMATCH!")));
    }

    [Fact]
    public void ParsesTablesAndSkipsUnaddressedOnes()
    {
        var rom = LoadBase();
        Assert.Equal("BASE", rom.RomId.XmlId);
        Assert.Equal("Acme", rom.RomId.Make);
        Assert.NotNull(rom.GetTable("scalar"));
        Assert.Null(rom.GetTable("NoAddress"));
        Assert.Contains("OutOfRange", rom.FaultyTables);
        Assert.Null(rom.GetTable("OutOfRange"));
        Assert.Equal("Cat//Sub", rom.GetTable("Scalar")!.Category);
        Assert.Equal("A scalar.", rom.GetTable("Scalar")!.Description);
    }

    [Fact]
    public void ScalarUsesPreferredScaleAndConverts()
    {
        var t = LoadBase().GetTable("Scalar")!;
        Assert.Equal("Metric", t.CurrentScale!.Category);
        Assert.Equal(500, t.Cells[0].BinValue);
        Assert.Equal(50.0, t.Cells[0].RealValue, 6);
        Assert.True(t.SetScaleByCategory("Standard"));
        Assert.Equal(122.0, t.Cells[0].RealValue, 6);
        Assert.Contains(t.Scales, s => s.IsRaw);
    }

    [Fact]
    public void EditsWriteThroughAndKeepAliasesInSync()
    {
        var rom = LoadBase();
        var scalar = rom.GetTable("Scalar")!;
        var alias = rom.GetTable("Alias")!;
        scalar.Cells[0].SetRealValue("60.0");
        Assert.Equal(600, scalar.Cells[0].BinValue);
        Assert.Equal(0x02, rom.Binary[0x10]);
        Assert.Equal(0x58, rom.Binary[0x11]);
        Assert.Equal(600, alias.Cells[0].BinValue);
        Assert.True(rom.IsDirty);
    }

    [Fact]
    public void ValuesAreClampedToStorageRange()
    {
        var rom = LoadBase();
        var curve = (Table2D)rom.GetTable("Curve")!;
        curve.Cells[0].SetRealValue("1000");
        Assert.Equal(255, curve.Cells[0].BinValue);
        curve.Cells[0].SetRealValue("-5");
        Assert.Equal(0, curve.Cells[0].BinValue);
    }

    [Fact]
    public void TwoDTableHasScaledAxis()
    {
        var curve = (Table2D)LoadBase().GetTable("Curve")!;
        Assert.Equal(4, curve.Cells.Length);
        Assert.Equal(4, curve.Axis.Cells.Length);
        Assert.Equal(5.0, curve.Cells[0].RealValue);
        Assert.Equal(1000, curve.Axis.Cells[0].RealValue);
        Assert.Equal("RPM", curve.Axis.CurrentScale!.Unit);
        Assert.Same(curve, curve.Axis.AxisParent);
    }

    [Fact]
    public void StaticAxisLabels()
    {
        var t = (Table2D)LoadBase().GetTable("StaticCurve")!;
        Assert.True(t.Axis.IsStaticDataTable);
        Assert.Equal(new[] { "1st", "2nd", "3rd" }, t.Axis.Cells.Select(c => c.StaticText ?? "").ToArray());
        Assert.Equal(-2, t.Cells[0].BinValue);
        t.Axis.Cells[0].IsSelected = true;
        Assert.False(t.Axis.Cells[0].IsSelected);
    }

    [Fact]
    public void ThreeDLayoutIsRowMajorOverX()
    {
        var map = (Table3D)LoadBase().GetTable("Map")!;
        Assert.Equal(3, map.Data3D.GetLength(0));
        Assert.Equal(2, map.Data3D.GetLength(1));
        Assert.Equal(1, map.Data3D[1, 0].BinValue);
        Assert.Equal(3, map.Data3D[0, 1].BinValue);
        Assert.Equal(5, map.Data3D[2, 1].BinValue);
        Assert.Equal([1.0, 2, 3], map.XAxis.Cells.Select(c => c.BinValue));
        Assert.Equal([10.0, 20], map.YAxis.Cells.Select(c => c.BinValue));
    }

    [Fact]
    public void SwapXYIsColumnMajor()
    {
        var map = (Table3D)LoadBase().GetTable("SwappedMap")!;
        Assert.Equal(1, map.Data3D[0, 1].BinValue);
        Assert.Equal(2, map.Data3D[1, 0].BinValue);
        Assert.Equal(5, map.Data3D[2, 1].BinValue);
    }

    [Fact]
    public void SwitchStates()
    {
        var rom = LoadBase();
        var sw = (TableSwitch)rom.GetTable("Mode")!;
        Assert.Equal("On", sw.ActiveState!.Name);
        PresetManager.Apply(sw, sw.Presets!.Presets.First(p => p.Name == "Off"));
        Assert.Equal(0, rom.Binary[0x70]);
        Assert.Equal(0, rom.Binary[0x71]);
        Assert.Equal("Off", sw.ActiveState!.Name);
    }

    [Fact]
    public void BitwiseSwitchBits()
    {
        var rom = LoadBase();
        var flags = (TableBitwiseSwitch)rom.GetTable("Flags")!;
        var a = flags.Presets!.Presets[0];
        var b = flags.Presets.Presets[1];
        Assert.False(PresetManager.IsActive(flags, a));
        Assert.True(PresetManager.IsActive(flags, b));
        PresetManager.Apply(flags, a);
        Assert.Equal(0b0000_1001, rom.Binary[0x72]);
        PresetManager.Clear(flags, b);
        Assert.Equal(0b0000_0001, rom.Binary[0x72]);
    }

    [Fact]
    public void MaskedTableOnlyTouchesMaskBits()
    {
        var rom = LoadBase();
        var t = rom.GetTable("Masked")!;
        Assert.Equal(0xA, t.Cells[0].BinValue);
        Assert.Equal(15, t.Cells[0].MaxAllowedBin);
        t.Cells[0].SetBinValue(3);
        Assert.Equal(0x35, rom.Binary[0x74]);
        t.Cells[0].SetBinValue(99);
        Assert.Equal(0xF5, rom.Binary[0x74]);
    }

    [Fact]
    public void FloatAndSignedLittleEndian()
    {
        var rom = LoadBase();
        Assert.Equal(Math.PI, rom.GetTable("FloatVal")!.Cells[0].BinValue, 5);
        var s = rom.GetTable("Signed16LE")!;
        Assert.Equal(-2, s.Cells[0].BinValue);
        s.Cells[0].SetBinValue(-300);
        Assert.Equal(-300, (short)(rom.Binary[0x7C] | rom.Binary[0x7D] << 8));
    }

    [Fact]
    public void UserLevelBlocksEditing()
    {
        var rom = LoadBase();
        var t = rom.GetTable("Level5")!;
        Assert.Throws<UserLevelException>(() => t.Cells[0].SetBinValue(8));
        CoreSettings.Current.UserLevel = 5;
        t.Cells[0].SetBinValue(8);
        Assert.Equal(8, rom.Binary[0x75]);
    }

    [Fact]
    public void ChildRomInheritsOverridesOmitsAndCopies()
    {
        var rom = TestData.LoadInline(Xml, MakeImage("TESTROM2"), "CHILD");
        Assert.Equal("CHILD", rom.RomId.XmlId);
        Assert.Equal(256, rom.RomId.FileSize);
        Assert.Equal("Acme", rom.RomId.Make);

        var scalar = rom.GetTable("Scalar")!;
        Assert.Equal(0x12, scalar.StorageAddress);
        Assert.Equal(100, scalar.Cells[0].BinValue);
        Assert.Equal(2, scalar.Scales.Count(s => !s.IsRaw));

        Assert.Null(rom.GetTable("Masked"));

        var copied = (Table2D)rom.GetTable("Copied")!;
        var curve = (Table2D)rom.GetTable("Curve")!;
        Assert.NotSame(curve, copied);
        Assert.Equal(0x80, copied.StorageAddress);
        Assert.Equal(0x30, copied.Axis.StorageAddress);
        Assert.Equal("%", copied.CurrentScale!.Unit);

        // Defined without an address in the base, given one by the child.
        Assert.Equal(0x90, rom.GetTable("NoAddress")!.StorageAddress);

        // Definition order is kept, inherited tables first.
        Assert.Equal("Scalar", rom.Tables[0].Name);
    }

    [Fact]
    public void RamOffsetComesFromFileSize()
    {
        const string xml = """
            <roms><rom>
              <romid><xmlid>R</xmlid><filesize>512b</filesize></romid>
              <table name="T" type="1D" storageaddress="0x110" storagetype="uint8"/>
            </rom></roms>
            """;
        var image = new byte[256];
        image[0x10] = 42;
        var rom = TestData.LoadInline(xml, image);
        Assert.Equal(256, rom.RomId.RamOffset);
        Assert.Equal(42, rom.GetTable("T")!.Cells[0].BinValue);
    }

    [Fact]
    public void BoschSubtractLayout()
    {
        const string xml = """
            <roms><rom>
              <romid><xmlid>R</xmlid><noRamOffset/></romid>
              <table name="Axis" type="1D" storageaddress="0x0" storagetype="uint8" sizex="3" dataLayout="bosch_subtract"/>
            </rom></roms>
            """;
        var image = new byte[] { 10, 20, 30, 0 };
        var rom = TestData.LoadInline(xml, image);
        var t = rom.GetTable("Axis")!;
        // value[i] = 256 - sum(stored[i..end])
        Assert.Equal([196.0, 206, 226], t.Cells.Select(c => c.BinValue));
        t.Cells[2].SetBinValue(216);
        Assert.Equal(40, image[2]);
        // Earlier cells keep their values.
        Assert.Equal([196.0, 206, 216], t.Cells.Select(c => c.BinValue));
        rom.RefreshAllCells();
        Assert.Equal([196.0, 206, 216], t.Cells.Select(c => c.BinValue));
    }

    [Fact]
    public void ApproximatesWhenToByteIsMissing()
    {
        const string xml = """
            <roms><rom>
              <romid><xmlid>R</xmlid><noRamOffset/></romid>
              <table name="T" type="1D" storageaddress="0x0" storagetype="uint8">
                <scaling units="x" expression="x*0.5+10" format="0.0"/>
              </table>
            </rom></roms>
            """;
        var image = new byte[4];
        var rom = TestData.LoadInline(xml, image);
        var t = rom.GetTable("T")!;
        Assert.Null(t.CurrentScale!.ByteExpression);
        t.Cells[0].SetRealValue("30");
        Assert.Equal(40, image[0]);
    }

    [Fact]
    public void LoopedBaseIsAnError()
    {
        const string xml = """
            <roms>
              <rom base="B"><romid><xmlid>A</xmlid></romid></rom>
              <rom base="A"><romid><xmlid>B</xmlid></romid></rom>
            </roms>
            """;
        var doc = DefinitionDocument.Parse(xml);
        Assert.Throws<DefinitionException>(() => new RomUnmarshaller(doc).Unmarshal(doc.Roms[0].Node));
    }

    [Fact]
    public void ShapesAreJudgedByValueCount()
    {
        var rom = LoadBase();
        Assert.Equal(TableShape.Scalar, rom.GetTable("Scalar")!.Shape);
        Assert.Equal(TableShape.Array, rom.GetTable("Curve")!.Shape);
        Assert.Equal("Array (4)", rom.GetTable("Curve")!.ShapeDescription);
        Assert.Equal(TableShape.Map, rom.GetTable("Map")!.Shape);
        Assert.Equal("Map 3 x 2", rom.GetTable("Map")!.ShapeDescription);
        Assert.Equal(TableShape.Switch, rom.GetTable("Mode")!.Shape);
        Assert.Equal(TableShape.Switch, rom.GetTable("Flags")!.Shape);
    }
}
