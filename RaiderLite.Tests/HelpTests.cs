using RaiderLite.Core.Help;

namespace RaiderLite.Tests;

public class HelpTests
{
    /// <summary>The app's help pages, embedded in this assembly by the project file.</summary>
    private static readonly HelpLibrary Shipped = HelpLibrary.FromAssembly(typeof(HelpTests).Assembly);

    /// <summary>The links the app opens: Help menu, F1 contexts and dialogs' help.</summary>
    public static TheoryData<string> AppLinks => new()
    {
        "overview", "keyboard-shortcuts", "tunes", "table-list", "graphs", "editing",
        "settings", "table-windows#switches", "table-windows#single-values",
        "definitions#the-definition-manager", "definitions#no-match", "rescale-axis#the-dialog",
        "tunes#new-car", "tunes#new-revisions", "tunes#revision-commands",
        "table-list#table-properties", "images#properties", "comparing#compare-images",
    };

    private static HelpTopic Parse(string markdown) => HelpTopic.Parse("page", markdown);

    [Fact]
    public void ParsesTitleSectionsAndAnchors()
    {
        var page = Parse("""
            # The Title

            ## Copy and paste
            ### A sub-section
            ## Explicit {#custom}
            """);
        Assert.Equal("The Title", page.Title);
        Assert.Equal(["copy-and-paste", "custom"], page.Sections.Select(s => s.Anchor));
        Assert.Equal("Explicit", page.Sections[1].Text);
        Assert.Equal(["the-title", "copy-and-paste", "a-sub-section", "custom"], page.Anchors);
        Assert.Equal(3, page.FindHeading("A-SUB-SECTION")?.Level);
    }

    [Fact]
    public void PageWithoutTitleIsRejected()
    {
        Assert.Throws<FormatException>(() => Parse("Just text.\n\n# Late title"));
    }

    [Fact]
    public void JoinsLinesIntoParagraphsListsAndNotes()
    {
        var page = Parse("""
            # T
            First line
            second line.

            - one
              continued
            - two
            lazy continuation

            1. first
            2. second

            > **Note:** a warning
            that wraps.
            After a blank? No: this continues the note too.

            Last paragraph.
            """);
        var blocks = page.Blocks.Skip(1).ToList();
        Assert.Equal("First line second line.", Text(Assert.IsType<HelpParagraph>(blocks[0]).Runs));
        var bullets = Assert.IsType<HelpList>(blocks[1]);
        Assert.False(bullets.Numbered);
        Assert.Equal(["one continued", "two lazy continuation"], bullets.Items.Select(Text));
        var numbered = Assert.IsType<HelpList>(blocks[2]);
        Assert.True(numbered.Numbered);
        Assert.Equal(["first", "second"], numbered.Items.Select(Text));
        var note = Assert.IsType<HelpNote>(blocks[3]);
        Assert.Equal("Note: a warning that wraps. After a blank? No: this continues the note too.", Text(note.Runs));
        Assert.Equal("Last paragraph.", Text(Assert.IsType<HelpParagraph>(blocks[4]).Runs));
        Assert.Equal(5, blocks.Count);
    }

    [Fact]
    public void ParsesTablesAndCode()
    {
        var page = Parse("""
            # T
            | Key | Action |
            |---|:---:|
            | **+** | Up \| more |
            | `x` | |

            ```
            folder\
              file.txt   {braces}
            ```
            """);
        var table = Assert.IsType<HelpTable>(page.Blocks[1]);
        Assert.Equal(["Key", "Action"], table.Header.Select(Text));
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["+", "Up | more"], table.Rows[0].Select(Text));
        Assert.Equal(HelpRunStyle.Bold, table.Rows[0][0].Single().Style);
        Assert.Equal(HelpRunStyle.Code, table.Rows[1][0].Single().Style);
        var code = Assert.IsType<HelpCode>(page.Blocks[2]);
        Assert.Equal("folder\\\n  file.txt   {braces}", code.Text);
    }

    [Fact]
    public void ParsesInlineStyles()
    {
        var runs = HelpTopic.ParseInline(@"Press **Ctrl+S** or `x` and see [Editing **values**](editing#undo), \*not bold\*, \[no link].");
        Assert.Equal(
        [
            new HelpRun("Press "),
            new HelpRun("Ctrl+S", HelpRunStyle.Bold),
            new HelpRun(" or "),
            new HelpRun("x", HelpRunStyle.Code),
            new HelpRun(" and see "),
            new HelpRun("Editing values", HelpRunStyle.None, "editing#undo"),
            new HelpRun(", *not bold*, [no link]."),
        ], runs);
    }

    [Fact]
    public void BoldSpansStyleCodeAndLinksInside()
    {
        var runs = HelpTopic.ParseInline("**a `b` [c](d)**");
        Assert.Equal(
        [
            new HelpRun("a ", HelpRunStyle.Bold),
            new HelpRun("b", HelpRunStyle.Bold | HelpRunStyle.Code),
            new HelpRun(" ", HelpRunStyle.Bold),
            new HelpRun("c", HelpRunStyle.Bold, "d"),
        ], runs);
    }

    [Fact]
    public void LooseBracketsAndStarsStayText()
    {
        Assert.Equal([new HelpRun("a [b] (c) * d")], HelpTopic.ParseInline("a [b] (c) * d"));
    }

    [Theory]
    [InlineData("03-editing.md", "editing")]
    [InlineData("13-tunes.md", "tunes")]
    [InlineData("no-number.md", "no-number")]
    [InlineData("12a-x.md", "12a-x")]
    public void IdComesFromFileName(string fileName, string id)
    {
        Assert.Equal(id, HelpLibrary.IdFromFileName(fileName));
    }

    private static HelpLibrary Sample() => HelpLibrary.Parse(
    [
        ("02-second.md", "# Second page\n\nSpeed and timing.\n\n## Timing\n\nIgnition timing table."),
        ("01-first.md", "# First page\n\nSee [the second](second#timing) and [here](#part).\n\n## Part\n\nFuel."),
        ("03-timing.md", "# Timing basics\n\nAbout timing."),
    ]);

    [Fact]
    public void OrdersPagesByFileName()
    {
        Assert.Equal(["first", "second", "timing"], Sample().Topics.Select(t => t.Id));
    }

    [Fact]
    public void ResolvesLinks()
    {
        var library = Sample();
        var first = library.Find("first")!;
        Assert.Equal(new HelpTarget(library.Find("second"), "timing", null), library.Resolve("second#timing", first));
        Assert.Equal(new HelpTarget(first, "part", null), library.Resolve("#part", first));
        Assert.Equal(new HelpTarget(library.Find("timing"), null, null), library.Resolve("TIMING", first));
        Assert.Equal(new HelpTarget(null, null, "https://www.romraider.com"), library.Resolve("https://www.romraider.com", first));
        Assert.Null(library.Resolve("missing", first));
        Assert.Null(library.Resolve("second#missing", first));
        Assert.Null(library.Resolve("#timing", first));
        Assert.Equal(["second#timing", "#part"], first.Links);
    }

    [Fact]
    public void SearchNeedsEveryWordAndRanksTitlesFirst()
    {
        var library = Sample();
        Assert.Equal(["timing", "second"], library.Search("timing").Select(t => t.Id));
        Assert.Equal(["second"], library.Search("ignition TIMING").Select(t => t.Id));
        Assert.Empty(library.Search("timing fuel"));
        Assert.Equal(3, library.Search("  ").Count);
    }

    [Fact]
    public void DuplicatePageNamesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => HelpLibrary.Parse([("01-a.md", "# A"), ("02-a.md", "# A again")]));
    }

    // ----- the help shipped with the app -----

    [Fact]
    public void ShippedHelpStartsWithTheOverview()
    {
        Assert.True(Shipped.Topics.Count >= 10, $"only {Shipped.Topics.Count} help pages were embedded");
        Assert.Equal("overview", Shipped.Topics[0].Id);
    }

    [Fact]
    public void ShippedLinksAllLeadSomewhere()
    {
        var broken = Shipped.Topics
            .SelectMany(t => t.Links.Where(link => Shipped.Resolve(link, t) == null).Select(link => $"{t.Id}: {link}"))
            .ToList();
        Assert.Empty(broken);
    }

    [Fact]
    public void ShippedAnchorsAreUniqueOnEachPage()
    {
        var duplicates = Shipped.Topics
            .SelectMany(t => t.Anchors.GroupBy(a => a, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => $"{t.Id}#{g.Key}"))
            .ToList();
        Assert.Empty(duplicates);
    }

    [Theory]
    [MemberData(nameof(AppLinks))]
    public void AppLinksLeadToShippedPages(string link)
    {
        Assert.NotNull(Shipped.Resolve(link, null));
    }

    private static string Text(IEnumerable<HelpRun> runs) => string.Concat(runs.Select(r => r.Text));
}
