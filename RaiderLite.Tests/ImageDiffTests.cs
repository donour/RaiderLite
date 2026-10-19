using RaiderLite.Core.Model;

namespace RaiderLite.Tests;

public class ImageDiffTests
{
    private static readonly string Xml = SyntheticDefinitionTests.Xml;

    public ImageDiffTests()
    {
        CoreSettings.Current = new CoreSettings();
    }

    private static Rom Template() => TestData.LoadInline(Xml, SyntheticDefinitionTests.MakeImage());

    private static string[] Names(ImageComparison c) => c.Tables.Select(t => t.Table.Name).ToArray();

    [Fact]
    public void IdenticalImagesHaveNoChanges()
    {
        var a = SyntheticDefinitionTests.MakeImage();
        var c = ImageDiff.Compare(Template(), a, (byte[])a.Clone());
        Assert.True(c.Identical);
        Assert.Empty(c.Tables);
    }

    [Fact]
    public void ReportsEveryTableSharingAChangedValue()
    {
        var a = SyntheticDefinitionTests.MakeImage();
        var b = (byte[])a.Clone();
        b[0x11] ^= 0x01;   // Scalar and its exact alias "Alias"
        var c = ImageDiff.Compare(Template(), a, b);
        Assert.Equal(["Scalar", "Alias"], Names(c));
        Assert.All(c.Tables, t => Assert.Equal(1, t.ChangedCells));
        Assert.Equal(1, c.ChangedBytes);
        Assert.Equal(0, c.ChangedBytesOutsideTables);
    }

    [Fact]
    public void AxisChangesCountTowardsTheirTable()
    {
        var a = SyntheticDefinitionTests.MakeImage();
        var b = (byte[])a.Clone();
        b[0x31] = 99;      // Curve's RPM axis
        b[0x60] = 9;       // the X axis shared by Map and SwappedMap
        var c = ImageDiff.Compare(Template(), a, b);
        Assert.Equal(["Curve", "Map", "SwappedMap"], Names(c));
    }

    [Fact]
    public void BitsOutsideAMaskAreNotAChange()
    {
        var a = SyntheticDefinitionTests.MakeImage();
        var b = (byte[])a.Clone();
        b[0x74] ^= 0x0F;   // "Masked" only uses 0xF0
        var c = ImageDiff.Compare(Template(), a, b);
        Assert.Empty(c.Tables);
        Assert.Equal(1, c.ChangedBytes);
        Assert.Equal(0, c.ChangedBytesOutsideTables);

        b[0x74] ^= 0x10;
        Assert.Equal(["Masked"], Names(ImageDiff.Compare(Template(), a, b)));
    }

    [Fact]
    public void CountsBytesNoTableCovers()
    {
        var a = SyntheticDefinitionTests.MakeImage();
        var b = (byte[])a.Clone();
        b[0xF0] = 1;
        b[0xF1] = 2;
        var c = ImageDiff.Compare(Template(), a, b);
        Assert.Empty(c.Tables);
        Assert.False(c.Identical);
        Assert.Equal(2, c.ChangedBytesOutsideTables);
    }

    [Fact]
    public void DifferentLengthsAreNotCompared()
    {
        var a = SyntheticDefinitionTests.MakeImage();
        var c = ImageDiff.Compare(Template(), a, a[..200]);
        Assert.False(c.SameSize);
        Assert.False(c.Identical);
        Assert.Empty(c.Tables);
    }

    [Fact]
    public void BoschSubtractChangesEarlierValues()
    {
        // Each value depends on the stored values after it, so changing the last stored byte
        // changes every value of the table.
        const string xml = """
            <roms><rom>
              <romid><xmlid>R</xmlid><internalidaddress>0x0</internalidaddress><internalidstring>RL</internalidstring><noRamOffset/></romid>
              <table name="B" type="1D" storageaddress="0x10" storagetype="uint8" sizex="5" dataLayout="bosch_subtract"/>
            </rom></roms>
            """;
        var a = new byte[64];
        a[0] = (byte)'R';
        a[1] = (byte)'L';
        new byte[] { 10, 20, 30, 40, 50 }.CopyTo(a, 0x10);
        var b = (byte[])a.Clone();
        b[0x14] = 51;
        var c = ImageDiff.Compare(TestData.LoadInline(xml, a), a, b);
        Assert.Equal(5, Assert.Single(c.Tables).ChangedCells);
    }

    [Fact]
    public void AgreesWithComparingFullyLoadedImages()
    {
        var template = Template();
        var rng = new Random(11);
        for (int i = 0; i < 300; i++)
        {
            var a = SyntheticDefinitionTests.MakeImage();
            var b = (byte[])a.Clone();
            int edits = rng.Next(1, 6);
            for (int e = 0; e < edits; e++)
                b[rng.Next(0x10, 0x80)] = (byte)rng.Next(256);

            var fast = ImageDiff.Compare(template, a, b).Tables.ToDictionary(t => t.Table.Name, t => t.ChangedCells);
            var full = ImageDiff.CompareRoms(TestData.LoadInline(Xml, a), TestData.LoadInline(Xml, b))
                .Where(t => t.Result == TableComparisonResult.Different)
                .ToDictionary(t => t.Name, t => t.ChangedCells);
            Assert.Equal(full.OrderBy(kv => kv.Key), fast.OrderBy(kv => kv.Key));
        }
    }

    [Fact]
    public void CompareRomsMatchesTablesByName()
    {
        var parent = TestData.LoadInline(Xml, SyntheticDefinitionTests.MakeImage("TESTROM1"));
        var child = TestData.LoadInline(Xml, SyntheticDefinitionTests.MakeImage("TESTROM2"), "CHILD");
        var results = ImageDiff.CompareRoms(parent, child).ToDictionary(r => r.Name);

        Assert.Equal(TableComparisonResult.Same, results["Curve"].Result);
        // The child reads "Scalar" from 0x12 (100) instead of 0x10 (500).
        Assert.Equal(TableComparisonResult.Different, results["Scalar"].Result);
        Assert.Equal(1, results["Scalar"].ChangedCells);
        Assert.Equal(TableComparisonResult.MissingInSecond, results["Masked"].Result);
        Assert.Equal(TableComparisonResult.MissingInFirst, results["Copied"].Result);
        Assert.Null(results["Copied"].First);
    }
}
