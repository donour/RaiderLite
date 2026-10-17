using RaiderLite.Core.Checksums;
using RaiderLite.Core.Model;

namespace RaiderLite.Tests;

public class ChecksumTests
{
    private static byte[] RandomImage(int size, int seed = 1)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static IChecksumManager Create(params (string Key, string Value)[] attrs) =>
        ChecksumFactory.Create(attrs.ToDictionary(a => a.Key, a => a.Value))!;

    [Theory]
    [InlineData("std")]
    [InlineData("alt")]
    public void NissanStdUpdateThenValidate(string type)
    {
        var data = RandomImage(0x1000);
        var cs = Create(("type", type), ("start", "0x0"), ("end", "0x800"), ("sumloc", "0x100"), ("xorloc", "0x104"));
        Assert.Equal(2, cs.NumberOfChecksums);
        cs.Update(data);
        Assert.Equal(2, cs.Validate(data));
        data[0x10]++;
        Assert.Equal(0, cs.Validate(data));
    }

    [Fact]
    public void NissanAlt2UpdateThenValidate()
    {
        var data = RandomImage(0x2000);
        var cs = Create(("type", "alt2"), ("start", "0x0"), ("end", "0x2000"), ("sumloc", "0x100"), ("xorloc", "0x104"), ("skiploc", "0x1000"));
        cs.Update(data);
        Assert.Equal(4, cs.Validate(data));
    }

    [Fact]
    public void ByteXor()
    {
        var data = RandomImage(256);
        var cs = Create(("type", "bytexor"), ("start", "0"), ("end", "100"), ("xorloc", "50"));
        cs.Update(data);
        Assert.Equal(1, cs.Validate(data));
        Assert.Equal(0, cs.Update(data));
    }

    [Fact]
    public void Copy()
    {
        var data = RandomImage(256);
        var cs = Create(("type", "copy"), ("start", "0x10"), ("end", "0x1F"), ("loc", "0x80"));
        Assert.Equal(1, cs.Update(data));
        Assert.Equal(data.AsSpan(0x10, 16).ToArray(), data.AsSpan(0x80, 16).ToArray());
        Assert.Equal(1, cs.Validate(data));
    }

    [Theory]
    [InlineData("motronicsingle")]
    [InlineData("motronicdouble")]
    public void Motronic(string type)
    {
        var data = RandomImage(512);
        var cs = Create(("type", type), ("start", "0"), ("end", "0x7F"), ("startsecond", "0x100"), ("endsecond", "0x17F"), ("loc", "0x1F0"));
        cs.Update(data);
        Assert.Equal(1, cs.Validate(data));
        data[0x120]++;
        Assert.Equal(type == "motronicdouble" ? 0 : 1, cs.Validate(data));
    }

    [Fact]
    public void UnknownTypeIsNull()
    {
        Assert.Null(ChecksumFactory.Create(new Dictionary<string, string> { ["type"] = "nope" }));
    }

    [Fact]
    public void ChecksumDeclaredInDefinitionIsUpdatedOnSave()
    {
        const string xml = """
            <roms><rom>
              <romid><xmlid>R</xmlid><noRamOffset/></romid>
              <checksum type="bytexor" start="0x0" end="0x10" xorloc="0xF"/>
              <table name="T" type="1D" storageaddress="0x2" storagetype="uint8"/>
            </rom></roms>
            """;
        CoreSettings.Current = new CoreSettings();
        var image = new byte[32];
        var rom = TestData.LoadInline(xml, image);
        Assert.Equal(1, rom.TotalChecksums);
        rom.GetTable("T")!.Cells[0].SetBinValue(0x5A);
        Assert.Equal(0, rom.ValidateChecksums());
        rom.PrepareForSave(out int updated);
        Assert.Equal(1, updated);
        Assert.Equal(0x5A, image[0xF]);
        Assert.Equal(1, rom.ValidateChecksums());
    }

    [Fact]
    public void SubaruChecksumFixTable()
    {
        const string xml = """
            <roms><rom>
              <romid><xmlid>R</xmlid><noRamOffset/></romid>
              <table name="Checksum Fix" type="Switch" storageaddress="0x400" sizey="24">
                <state name="on" data="00"/>
              </table>
              <table name="T" type="1D" storageaddress="0x10" storagetype="uint8"/>
            </rom></roms>
            """;
        CoreSettings.Current = new CoreSettings();
        var image = new byte[0x600];
        // Two records: [0x000,0x100) and [0x100,0x200)
        void Put(int at, int v) => System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(image.AsSpan(at), v);
        Put(0x400, 0x000); Put(0x404, 0x100);
        Put(0x40C, 0x100); Put(0x410, 0x200);
        new SubaruChecksumFix(new Table1D { StorageAddress = 0x400, DataSize = 24 }).Recalculate(image);

        var rom = TestData.LoadInline(xml, image);
        var fix = rom.ChecksumManagers.OfType<SubaruChecksumFix>().Single();
        Assert.True(fix.ValidAtLoad);
        Assert.Equal(2, rom.ValidateChecksums());

        rom.GetTable("T")!.Cells[0].SetBinValue(99);
        Assert.Equal(0, rom.ValidateChecksums());
        rom.PrepareForSave(out _);
        Assert.Equal(2, rom.ValidateChecksums());
    }
}
