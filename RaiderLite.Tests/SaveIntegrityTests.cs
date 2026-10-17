using RaiderLite.Core.Definitions;
using RaiderLite.Core.Model;

namespace RaiderLite.Tests;

/// <summary>
/// The image must always hold exactly what is shown: edits change only their own bytes, every
/// cell sharing those bytes is kept in sync, and undo restores the original image byte for byte.
/// </summary>
public class SaveIntegrityTests
{
    public SaveIntegrityTests()
    {
        CoreSettings.Current = new CoreSettings { UserLevel = 5 };
    }

    private static string Rom(string tables, string extraRomId = "") => $"""
        <roms><rom>
          <romid><xmlid>R</xmlid><internalidaddress>0x0</internalidaddress><internalidstring>RL</internalidstring><noRamOffset/>{extraRomId}</romid>
          {tables}
        </rom></roms>
        """;

    private static byte[] Image(int seed = 1, int size = 256)
    {
        var image = new byte[size];
        new Random(seed).NextBytes(image);
        image[0] = (byte)'R';
        image[1] = (byte)'L';
        return image;
    }

    /// <summary>Every cell's value equals what a fresh load of the current image shows.</summary>
    private static void AssertMatchesFreshLoad(Rom rom, string xml)
    {
        var fresh = TestData.LoadInline(xml, (byte[])rom.Binary.Clone());
        foreach (var table in rom.Tables)
        {
            var shown = table.AllCells.Where(c => !c.IsStatic).Select(c => c.BinValue).ToList();
            var stored = fresh.GetTable(table.Name)!.AllCells.Where(c => !c.IsStatic).Select(c => c.BinValue).ToList();
            Assert.Equal(stored, shown);
        }
    }

    private static void UndoAll(Rom rom)
    {
        while (rom.History.CanUndo)
            rom.History.Undo();
    }

    [Fact]
    public void PartlyOverlappingTablesStayInSyncAndUndoRestoresTheImage()
    {
        // A uint16 over two separate uint8 values (as in the Evora GT430 definition).
        var xml = Rom("""
            <table name="Wide" type="1D" storageaddress="0x10" storagetype="uint16"/>
            <table name="Low" type="1D" storageaddress="0x11" storagetype="uint8"/>
            <table name="High" type="1D" storageaddress="0x10" storagetype="uint8"/>
            """);
        var original = Image();
        var rom = TestData.LoadInline(xml, (byte[])original.Clone());
        var wide = rom.GetTable("Wide")!.Cells[0];
        var low = rom.GetTable("Low")!.Cells[0];

        wide.SetBinValue(0x1234);
        Assert.Equal(0x34, low.BinValue);
        Assert.Equal(0x12, rom.GetTable("High")!.Cells[0].BinValue);
        low.SetBinValue(0x99);
        Assert.Equal(0x1299, wide.BinValue);
        AssertMatchesFreshLoad(rom, xml);

        UndoAll(rom);
        Assert.Equal(original, rom.Binary);
        Assert.DoesNotContain(rom.Tables.SelectMany(t => t.AllCells), c => c.IsChanged);
        Assert.Contains(rom.Warnings, w => w.Contains("partly overlap", StringComparison.Ordinal));
    }

    [Fact]
    public void ExactAliasesAndMaskedFieldsAreNotReportedAsOverlaps()
    {
        var rom = TestData.LoadInline(Rom("""
            <table name="A" type="1D" storageaddress="0x10" storagetype="uint16"/>
            <table name="B" type="1D" storageaddress="0x10" storagetype="uint16"/>
            <table name="Hi" type="1D" storageaddress="0x20" storagetype="uint8" mask="F0"/>
            <table name="Lo" type="1D" storageaddress="0x20" storagetype="uint8" mask="0F"/>
            """), Image());
        Assert.DoesNotContain(rom.Warnings, w => w.Contains("overlap", StringComparison.Ordinal));
        rom.GetTable("Hi")!.Cells[0].SetBinValue(0xA);
        rom.GetTable("Lo")!.Cells[0].SetBinValue(0x5);
        Assert.Equal(0xA5, rom.Binary[0x20]);
    }

    [Fact]
    public void FloatValuesShowExactlyWhatIsStored()
    {
        var xml = Rom("""<table name="F" type="1D" storageaddress="0x10" storagetype="float"/>""");
        var rom = TestData.LoadInline(xml, Image());
        var cell = rom.GetTable("F")!.Cells[0];
        cell.SetBinValue(43.223);
        Assert.Equal((double)43.223f, cell.BinValue);
        AssertMatchesFreshLoad(rom, xml);
    }

    [Fact]
    public void FractionalRawValuesAreRoundedNotTruncatedBehindTheDisplay()
    {
        var xml = Rom("""<table name="U" type="1D" storageaddress="0x10" storagetype="uint8"/>""");
        var rom = TestData.LoadInline(xml, Image());
        var cell = rom.GetTable("U")!.Cells[0];
        cell.SetBinValue(12.7);
        Assert.Equal(13, cell.BinValue);
        Assert.Equal(13, rom.Binary[0x10]);
    }

    [Theory]
    [InlineData("big")]
    [InlineData("little")]
    public void Movi20WritesOnlyTheImmediateAndKeepsTheInstruction(string endian)
    {
        // 0x0F 0x90 0x12 0x34: MOVI20 R15 with immediate 0x91234; the low nibble of 0x90 is opcode.
        var xml = Rom($"""
            <table name="M" type="1D" storageaddress="0x11" storagetype="movi20" endian="{endian}"/>
            <table name="S" type="1D" storageaddress="0x21" storagetype="movi20s" endian="{endian}"/>
            """);
        var image = Image();
        image[0x10] = 0x0F; image[0x11] = 0x90; image[0x12] = 0x12; image[0x13] = 0x34;
        image[0x20] = 0x0E; image[0x21] = 0x31; image[0x22] = 0x00; image[0x23] = 0x10;  // MOVI20S
        var original = (byte[])image.Clone();
        var rom = TestData.LoadInline(xml, image);
        var m = rom.GetTable("M")!.Cells[0];
        var s = rom.GetTable("S")!.Cells[0];
        Assert.Equal(unchecked((int)0xFFF91234), m.BinValue);   // sign-extended 20 bits
        Assert.Equal(0x30010 << 8, s.BinValue);

        m.SetBinValue(0x12345);
        Assert.Equal(new byte[] { 0x0F, 0x10, 0x23, 0x45 }, rom.Binary[0x10..0x14]);
        s.SetBinValue(1000);   // MOVI20S holds multiples of 256
        Assert.Equal(1024, s.BinValue);
        Assert.Equal(0x01, rom.Binary[0x21] & 0x0F);   // still MOVI20S
        AssertMatchesFreshLoad(rom, xml);

        UndoAll(rom);
        Assert.Equal(original, rom.Binary);
    }

    [Fact]
    public void LittleEndianBitwiseSwitchFlagsReadBackAsSet()
    {
        var rom = TestData.LoadInline(Rom("""
            <table name="Bits" type="BitwiseSwitch" storageaddress="0x10" storagetype="uint16" endian="little">
              <bit name="b1" position="1"/><bit name="b9" position="9"/>
            </table>
            """), Image());
        var table = rom.GetTable("Bits")!;
        foreach (var flag in table.Presets.Presets)
        {
            PresetManager.Apply(table, flag);
            Assert.True(PresetManager.IsActive(table, flag), flag.Name);
            PresetManager.Clear(table, flag);
            Assert.False(PresetManager.IsActive(table, flag), flag.Name);
        }
    }

    [Fact]
    public void BoschSubtractEditsAlwaysReadBack()
    {
        var xml = Rom("""
            <table name="B" type="2D" storageaddress="0x10" storagetype="uint8" sizex="5" dataLayout="bosch_subtract">
              <table name="x" type="X Axis" storageaddress="0x20" storagetype="uint8"/>
            </table>
            """);
        var image = Image();
        byte[] stored = [10, 20, 30, 40, 50];   // values 106, 116, 136, 166, 206
        stored.CopyTo(image, 0x10);
        var original = (byte[])image.Clone();
        var rom = TestData.LoadInline(xml, image);
        var cells = rom.GetTable("B")!.Cells;
        Assert.Equal(new double[] { 106, 116, 136, 166, 206 }, cells.Select(c => c.BinValue));

        var rng = new Random(3);
        for (int i = 0; i < 200; i++)
        {
            cells[rng.Next(cells.Length)].SetBinValue(rng.Next(0, 256));
            AssertMatchesFreshLoad(rom, xml);
        }
        UndoAll(rom);
        Assert.Equal(original, rom.Binary);
    }

    [Fact]
    public void DebugTablesAreReadOnlyWhenNotSavedAndUndoAlwaysWrites()
    {
        var xml = Rom("""<table name="D" type="1D" storageaddress="0x10" storagetype="uint8" userlevel="5"/>""");
        var original = Image();
        var rom = TestData.LoadInline(xml, (byte[])original.Clone());
        var table = rom.GetTable("D")!;
        var cell = table.Cells[0];

        CoreSettings.Current.SaveDebugTables = false;
        Assert.NotNull(table.ReadOnlyReason);
        Assert.Throws<UserLevelException>(() => cell.SetBinValue(cell.BinValue + 1));
        Assert.Equal(original, rom.Binary);

        // Edited while allowed, then undone after the level was lowered: the bytes still revert.
        CoreSettings.Current.SaveDebugTables = true;
        cell.SetBinValue((cell.BinValue + 1) % 256);
        CoreSettings.Current.UserLevel = 1;
        UndoAll(rom);
        Assert.Equal(original, rom.Binary);
        AssertMatchesFreshLoad(rom, xml);
    }

    [Fact]
    public void TheMostSpecificDefinitionIdWins()
    {
        // "LOTUS EVORA " is a prefix of the GT430 id: file order must not decide.
        var doc = DefinitionDocument.Parse("""
            <roms>
              <rom><romid><xmlid>GENERIC</xmlid><internalidaddress>0x0</internalidaddress><internalidstring>LOTUS EVORA </internalidstring></romid></rom>
              <rom><romid><xmlid>GT430</xmlid><internalidaddress>0x0</internalidaddress><internalidstring>LOTUS EVORA GT430</internalidstring></romid></rom>
            </roms>
            """);
        var gt430 = System.Text.Encoding.ASCII.GetBytes("LOTUS EVORA GT430 MAN MY19 ROW".PadRight(64));
        var generic = System.Text.Encoding.ASCII.GetBytes("LOTUS EVORA S1".PadRight(64));
        Assert.Equal("GT430", doc.FindMatch(gt430)!.Id.XmlId);
        Assert.Equal(["GT430", "GENERIC"], doc.FindMatches(gt430).Select(r => r.Id.XmlId));
        Assert.Equal("GENERIC", doc.FindMatch(generic)!.Id.XmlId);
    }

    [Fact]
    public void ChecksumsAreRecomputedOverEditsAndValidateAfterSaving()
    {
        var xml = Rom("""
            <checksum type="std" start="0x40" end="0x7F" sumloc="0x80" xorloc="0x84"/>
            <checksum type="bytexor" start="0x90" end="0x9F" xorloc="0xA0"/>
            <table name="InStd" type="1D" storageaddress="0x50" storagetype="uint16"/>
            <table name="InXor" type="1D" storageaddress="0x95" storagetype="uint8"/>
            """);
        var rom = TestData.LoadInline(xml, Image(size: 512));
        rom.GetTable("InStd")!.Cells[0].SetBinValue(0x4242);
        rom.GetTable("InXor")!.Cells[0].SetBinValue(0x17);
        var saved = rom.PrepareForSave(out _);
        Assert.Empty(rom.ChecksumErrors);
        var reloaded = TestData.LoadInline(xml, saved);
        Assert.Equal(reloaded.TotalChecksums, reloaded.ValidateChecksums());
    }

    // ----- real images: random edits on every table -----

    [RequiresFilesFact(nameof(TestData.T4eDef), nameof(TestData.T4eBlackCalrom))]
    public void RandomEditsOnEveryT4eTableAreExactAndUndoable() =>
        RandomEditsAreExactAndUndoable(TestData.T4eDef, TestData.T4eBlackCalrom);

    [RequiresFilesFact(nameof(TestData.EvoraGt430Def), nameof(TestData.EvoraGt430Calrom))]
    public void RandomEditsOnEveryEvoraGt430TableAreExactAndUndoable() =>
        RandomEditsAreExactAndUndoable(TestData.EvoraGt430Def, TestData.EvoraGt430Calrom);

    private static void RandomEditsAreExactAndUndoable(string defPath, string imagePath)
    {
        var original = File.ReadAllBytes(imagePath);
        var doc = DefinitionDocument.Load(defPath);
        var info = doc.FindMatch(original)!;
        var rom = RomUnmarshaller.Load(doc, info.Node, (byte[])original.Clone(), imagePath);
        var rng = new Random(42);

        foreach (var table in rom.Tables)
        {
            var before = (byte[])rom.Binary.Clone();
            var cells = table.AllCells.Where(c => !c.IsStatic).ToList();
            var edited = cells.Count <= 8 ? cells : cells.OrderBy(_ => rng.Next()).Take(8).ToList();
            using (rom.History.Begin(table.Name))
            {
                foreach (var cell in edited)
                    cell.SetBinValue(Math.Floor(cell.MinAllowedBin + rng.NextDouble() * (cell.MaxAllowedBin - cell.MinAllowedBin + 1)));
            }
            // Only the edited cells' bytes changed.
            var allowed = edited.SelectMany(c => Enumerable.Range(c.Address, StorageType.ByteLength(c.Table.StorageType))).ToHashSet();
            for (int i = 0; i < before.Length; i++)
                Assert.True(before[i] == rom.Binary[i] || allowed.Contains(i), $"{table.Name}: byte 0x{i:X} changed");
        }

        // What is shown is what is stored, for every table (including overlapping ones).
        var fresh = RomUnmarshaller.Load(doc, info.Node, (byte[])rom.Binary.Clone(), imagePath);
        foreach (var table in rom.Tables)
        {
            Assert.Equal(
                fresh.GetTable(table.Name)!.AllCells.Where(c => !c.IsStatic).Select(c => c.BinValue),
                table.AllCells.Where(c => !c.IsStatic).Select(c => c.BinValue));
        }

        var edits = (byte[])rom.Binary.Clone();
        UndoAll(rom);
        Assert.Equal(original, rom.Binary);
        while (rom.History.CanRedo)
            rom.History.Redo();
        Assert.Equal(edits, rom.Binary);
    }
}
