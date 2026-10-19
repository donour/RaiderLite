using RaiderLite.Core.Model;
using RaiderLite.Core.Tunes;

namespace RaiderLite.Tests;

public sealed class ChangelogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RaiderLiteTests", Guid.NewGuid().ToString("N"));

    public ChangelogTests()
    {
        CoreSettings.Current = new CoreSettings();
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Cleaned up with the temp folder eventually.
        }
    }

    [Theory]
    [InlineData("inj: afr base", "* inj afr base: ")]
    [InlineData("sensor: maf scaling", "* sensor maf scaling: ")]
    [InlineData("Rev Limit", "* Rev Limit: ")]
    public void NoteLinesFollowTheBulletStyle(string table, string expected) => Assert.Equal(expected, Changelog.NoteLine(table));

    [Theory]
    // Lines from the 2011 EvoraNA notes against the tables those revisions changed.
    [InlineData("* inj afr base: rescale high load", "inj: afr base", true)]
    [InlineData("* afr base: set power peak target to 12.2", "inj: afr base", true)]
    [InlineData("* sensor_maf_scaling: scale for 3.5\" FMW intake", "sensor: maf scaling", true)]
    [InlineData("* maf scaling: update from logs", "sensor: maf scaling", true)]
    [InlineData("* CAL_obd_P0171/2_learn_time_threshold: divide by 3", "obd: P0171 learn time threshold", true)]
    [InlineData("* Disable P0139, P0159", "obd: ii P0139", true)]
    [InlineData("* ign mbt : add more timing at 3500rpm", "ign: mbt modeled", false)]
    [InlineData("* inj afr base: rescale top", "sensor: maf scaling", false)]
    [InlineData("", "inj: efficiency", false)]
    public void NotesMentionTablesLoosely(string notes, string table, bool expected) => Assert.Equal(expected, Changelog.Mentions(notes, table));

    [Fact]
    public void MentionsLookAtOneLineAtATime()
    {
        // "maf" on one line and "scaling" on another isn't a mention.
        Assert.False(Changelog.Mentions("* maf: up\n* scaling of fans", "sensor: maf scaling"));
        Assert.True(Changelog.Mentions("first\r\n* maf scaling: up\r\n", "sensor: maf scaling"));
    }

    [Fact]
    public void SuggestsLinesOnlyForUnmentionedTables()
    {
        var lines = Changelog.SuggestedLines("* inj efficiency: retune", ["inj: efficiency", "load: alphaN base", "load: alphaN base"]);
        Assert.Equal("* load alphaN base: " + Environment.NewLine, lines);
    }

    [Fact]
    public void BuildsTheCumulativeChangelogOfALineage()
    {
        var stockPath = Path.Combine(_root, "stock.bin");
        File.WriteAllBytes(stockPath, [1, 2, 3]);
        File.SetLastWriteTime(stockPath, new DateTime(2025, 7, 24));
        var car = Car.Create(Path.Combine(_root, "car"), "Car", "X", stockPath);
        var a = car.NewRevision(car.Revisions[0], "Base", new DateTime(2026, 2, 10));
        car.WriteNotes(a, "* revlimit: tour set to 7200\n");
        var side = car.NewRevision(a, "Side branch", new DateTime(2026, 3, 13));
        car.WriteNotes(side, "* closed loop test");
        var b = car.NewRevision(a, "Rad fan", new DateTime(2026, 5, 18));
        car.WriteNotes(b, "* rad fan: lower activation temps");

        var text = Changelog.Build(car, b, r => car.ReadNotes(r));
        var expected = string.Join(Environment.NewLine,
            "20260518 - 003 Rad fan", "", "* rad fan: lower activation temps", "",
            "20260210 - 001 Base", "", "* revlimit: tour set to 7200", "",
            "20250724 - 000 Stock") + Environment.NewLine;
        Assert.Equal(expected, text);
        Assert.DoesNotContain("closed loop", text);
    }

    [Fact]
    public void AReadOnlyImageRefusesEdits()
    {
        var rom = TestData.LoadInline(SyntheticDefinitionTests.Xml, SyntheticDefinitionTests.MakeImage());
        var table = rom.GetTable("Scalar")!;
        int changes = 0;
        rom.ReadOnlyChanged += (_, _) => changes++;
        rom.ReadOnlyReason = "005 is locked";
        Assert.Equal(1, changes);
        Assert.Equal("005 is locked", table.ReadOnlyReason);

        var before = (byte[])rom.Binary.Clone();
        var ex = Assert.Throws<ImageReadOnlyException>(() => table.Cells[0].SetBinValue(123));
        Assert.Equal("005 is locked", ex.Reason);
        Assert.Equal(before, rom.Binary);
        Assert.False(rom.IsDirty);
        Assert.NotNull(AxisRescaler.CannotRescale(rom.GetTable("Curve")!, ((Table2D)rom.GetTable("Curve")!).Axis));

        // Setting a cell to the value it has isn't an edit.
        table.Cells[0].SetBinValue(table.Cells[0].BinValue);

        rom.ReadOnlyReason = null;
        table.Cells[0].SetBinValue(123);
        Assert.Equal(123, table.Cells[0].BinValue);
        Assert.Null(table.ReadOnlyReason);
    }
}
