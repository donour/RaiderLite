using RaiderLite.Core.Layout;

namespace RaiderLite.Tests;

public class TableTreeGroupingTests
{
    /// <summary>Each name with its generated folders, joined as "folder/folder".</summary>
    private static Dictionary<string, string> Paths(IReadOnlyList<string> names, int threshold = 5)
    {
        var paths = TableTreeGrouping.Group(names, threshold);
        Assert.Equal(names.Count, paths.Length);
        return names.Zip(paths).ToDictionary(p => p.First, p => string.Join("/", p.Second));
    }

    /// <summary>Names like a DTC block: "obd: ii P0101", plus its counters and state.</summary>
    private static List<string> DtcNames(params string[] codes) =>
        [.. codes.SelectMany(c => new[] { $"obd: ii {c}", $"obd: ii {c} fail counter", $"obd: ii {c} pass counter", $"obd: ii {c} dtc state" })];

    [Fact]
    public void SmallCategoriesAreLeftAlone()
    {
        var names = new[] { "fuel: a x", "fuel: a y", "fuel: b" };
        Assert.All(Paths(names, threshold: 5).Values, p => Assert.Equal("", p));
        Assert.Empty(TableTreeGrouping.Group([]));
    }

    [Fact]
    public void GroupsByTheWordAfterTheSharedPrefix()
    {
        var paths = Paths(DtcNames("P0101", "P0102"));
        // "obd: ii" is in every name, so it makes no folder.
        Assert.Equal("P0101", paths["obd: ii P0101"]);
        Assert.Equal("P0101", paths["obd: ii P0101 fail counter"]);
        Assert.Equal("P0102", paths["obd: ii P0102 dtc state"]);
    }

    [Fact]
    public void AWordOnlyOneTableHasMakesNoFolder()
    {
        var names = new[] { "ign: comp a", "ign: comp b", "ign: comp c", "ign: dwell", "ign: mbt", "ign: retard" };
        var paths = Paths(names);
        Assert.Equal("comp", paths["ign: comp a"]);
        Assert.Equal("", paths["ign: dwell"]);
        Assert.Equal("", paths["ign: retard"]);
    }

    [Fact]
    public void WordsCompareIgnoringCaseAndKeepTheFirstSpelling()
    {
        var names = new[] { "x: Comp a", "x: comp b", "x: COMP c", "x: d", "x: e", "x: f" };
        Assert.All(names.Take(3), n => Assert.Equal("Comp", Paths(names)[n]));
    }

    [Fact]
    public void ANameThatIsOnlyTheSharedPrefixStaysAtTheTop()
    {
        // Every name starts "fuel", so grouping starts at the word after it.
        var names = new[] { "fuel", "fuel trim a", "fuel trim b", "fuel cut a", "fuel cut b", "fuel prime" };
        var paths = Paths(names);
        Assert.Equal("", paths["fuel"]);
        Assert.Equal("trim", paths["fuel trim a"]);
        Assert.Equal("cut", paths["fuel cut b"]);
        Assert.Equal("", paths["fuel prime"]);
    }

    [Fact]
    public void LargeFoldersSplitAgainOnTheNextWord()
    {
        var names = new List<string> { "ign: dwell", "ign: mbt" };
        names.AddRange(Enumerable.Range(0, 4).Select(i => $"ign: comp coolant {i}"));
        names.AddRange(Enumerable.Range(0, 4).Select(i => $"ign: comp air {i}"));
        var paths = Paths(names);
        Assert.Equal("comp/coolant", paths["ign: comp coolant 0"]);
        Assert.Equal("comp/air", paths["ign: comp air 3"]);
        Assert.Equal("", paths["ign: mbt"]);
    }

    [Fact]
    public void TooManyFoldersAreBucketedByTheirFirstCharacters()
    {
        string[] codes = ["P0101", "P0102", "P0103", "P0201", "P0202", "P0203", "P0301", "P0302", "P0303"];
        var paths = Paths(DtcNames(codes));
        Assert.Equal("P010…/P0101", paths["obd: ii P0101 pass counter"]);
        Assert.Equal("P020…/P0203", paths["obd: ii P0203"]);
        Assert.Equal("P030…/P0301", paths["obd: ii P0301 dtc state"]);
    }

    [Fact]
    public void ABucketOfOneIsNotAFolder()
    {
        string[] codes = ["P0101", "P0102", "P0103", "P0201", "P0202", "P0203", "U0401"];
        var paths = Paths(DtcNames(codes));
        Assert.Equal("U0401", paths["obd: ii U0401 fail counter"]);
        Assert.Equal("P010…/P0102", paths["obd: ii P0102"]);
    }

    [Fact]
    public void BucketsAreNamedForAllTheirWordsShare()
    {
        var names = new List<string>();
        foreach (var word in new[] { "freeze", "freeze2", "idle", "load", "misfire", "warmup" })
            names.AddRange([$"obd: {word} a", $"obd: {word} b"]);
        var paths = Paths(names);
        Assert.Equal("freeze…/freeze", paths["obd: freeze a"]);
        Assert.Equal("freeze…/freeze2", paths["obd: freeze2 b"]);
        Assert.Equal("idle", paths["obd: idle a"]);
    }

    [Fact]
    public void ThresholdHasAFloor()
    {
        var names = DtcNames("P0101");
        Assert.All(TableTreeGrouping.Group(names, threshold: 0), p => Assert.Empty(p));
    }

    [Fact]
    public void LargeRealisticCategoryEndsUpBrowsable()
    {
        // About 200 DTCs in several families, as in a T6 "obd" category.
        var codes = new List<string>();
        foreach (var family in new[] { "P00", "P01", "P02", "P03", "P04", "P21", "U01" })
            codes.AddRange(Enumerable.Range(0, 30).Select(i => $"{family}{i:00}"));
        var names = DtcNames([.. codes]);
        names.AddRange(["obd: ii fuel type", "obd: ii standards supported"]);
        var paths = TableTreeGrouping.Group(names, TableTreeGrouping.DefaultThreshold);

        // No folder lists more than the threshold of entries (sub-folders plus tables).
        var children = new Dictionary<string, HashSet<string>>();
        for (int i = 0; i < names.Count; i++)
        {
            string parent = "";
            foreach (var folder in paths[i])
            {
                Add(parent, "/" + folder);
                parent += "/" + folder;
            }
            Add(parent, names[i]);
        }
        Assert.All(children, c => Assert.True(c.Value.Count <= TableTreeGrouping.DefaultThreshold, $"{c.Key}: {c.Value.Count}"));
        Assert.True(paths.Max(p => p.Length) <= 3);

        void Add(string parent, string child)
        {
            if (!children.TryGetValue(parent, out var set))
                children[parent] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(child);
        }
    }
}
