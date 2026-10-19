using RaiderLite.Core.Tunes;

namespace RaiderLite.Tests;

public sealed class CarTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RaiderLiteTests", Guid.NewGuid().ToString("N"));
    private readonly string _stock;

    public CarTests()
    {
        Directory.CreateDirectory(_root);
        _stock = Path.Combine(_root, "STOCK_TAB.cpt");
        File.WriteAllBytes(_stock, [1, 2, 3, 4, 5, 6, 7, 8]);
        File.SetLastWriteTime(_stock, new DateTime(2025, 7, 24, 12, 34, 56, 789));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file is still open somewhere; the temp folder gets cleaned up eventually.
        }
    }

    private string CarFolder => Path.Combine(_root, "Library", "2011 Evora NA");

    private Car NewCar() => Car.Create(CarFolder, "2011 Evora NA", "T6-V00BU", _stock);

    [Fact]
    public void CreateCopiesTheStockImageIntoRevisionZero()
    {
        var car = NewCar();
        var stock = Assert.Single(car.Revisions);
        Assert.Equal(0, stock.Number);
        Assert.Null(stock.Parent);
        Assert.Equal("000 2025-07-24 Stock", stock.Folder);
        Assert.Equal("STOCK_TAB.cpt", stock.Image);
        Assert.Equal(new DateTime(2025, 7, 24, 12, 34, 56), stock.Created);
        Assert.Equal(File.ReadAllBytes(_stock), File.ReadAllBytes(stock.ImagePath));
        Assert.Equal(Car.Hash(File.ReadAllBytes(_stock)), stock.Sha256);
        Assert.True(Car.IsCar(CarFolder));
        Assert.Equal(LockReason.Stock, car.LockReasonOf(stock));
    }

    [Fact]
    public void CreateRefusesAFolderInUse()
    {
        Directory.CreateDirectory(CarFolder);
        File.WriteAllText(Path.Combine(CarFolder, "something.txt"), "x");
        Assert.Throws<IOException>(NewCar);
    }

    [Fact]
    public void IndexRoundTrips()
    {
        var car = NewCar();
        var r1 = car.NewRevision(car.Revisions[0], "Silver car base", new DateTime(2026, 2, 10, 16, 12, 30, 500));
        var r2 = car.NewRevision(r1, "Too lean", new DateTime(2026, 8, 3, 22, 15, 0));
        car.SetRejected(r2, "too lean");
        car.SetHidden(r2, true);

        var reopened = Car.Open(CarFolder);
        Assert.Equal("2011 Evora NA", reopened.Name);
        Assert.Equal("T6-V00BU", reopened.Definition);
        Assert.Equal([0, 1, 2], reopened.Revisions.Select(r => r.Number));
        var o1 = reopened.Get(1)!;
        Assert.Equal(r1.Folder, o1.Folder);
        Assert.Equal(r1.Image, o1.Image);
        Assert.Equal(r1.Sha256, o1.Sha256);
        Assert.Equal(new DateTime(2026, 2, 10, 16, 12, 30), o1.Created);   // whole seconds
        Assert.Equal(0, o1.Parent);
        Assert.Equal("Silver car base", o1.Label);
        Assert.Null(o1.Rejected);
        var o2 = reopened.Get(2)!;
        Assert.Equal(1, o2.Parent);
        Assert.Equal("too lean", o2.Rejected);
        Assert.True(o2.Hidden);
        Assert.Same(reopened, o2.Car);
    }

    [Fact]
    public void IndexLeavesOutEmptyFields()
    {
        var car = NewCar();
        var json = File.ReadAllText(car.IndexPath);
        Assert.Contains("\"format\": 1", json);
        Assert.DoesNotContain("hidden", json);
        Assert.DoesNotContain("rejected", json);
        Assert.DoesNotContain("\"parent\"", json);
        Assert.Contains("\"number\": 0", json);
    }

    [Fact]
    public void NewRevisionCopiesTheParentImage()
    {
        var car = NewCar();
        var stock = car.Revisions[0];
        var r = car.NewRevision(stock, "Rev limit 7200", new DateTime(2026, 2, 10, 9, 0, 0));
        Assert.Equal(1, r.Number);
        Assert.Equal(0, r.Parent);
        Assert.Equal("001 2026-02-10 Rev limit 7200", r.Folder);
        Assert.Equal("20260210-2011-evora-na-001.cpt", r.Image);
        Assert.Equal(File.ReadAllBytes(stock.ImagePath), File.ReadAllBytes(r.ImagePath));
        Assert.True(car.IsNoChange(r));
        Assert.Equal(LockReason.None, car.LockReasonOf(r));
        Assert.Equal([r], car.ChildrenOf(stock));

        var child = car.NewRevision(r, "Next");
        Assert.Equal(LockReason.HasChildren, car.LockReasonOf(r));
        Assert.Equal([child, r, stock], car.Lineage(child));
    }

    [Fact]
    public void SavingAnImageUpdatesItsHash()
    {
        var car = NewCar();
        var r = car.NewRevision(car.Revisions[0], "Edit");
        File.WriteAllBytes(r.ImagePath, [9, 2, 3, 4, 5, 6, 7, 8]);
        Assert.Equal(ImageState.Changed, car.CheckImage(r));
        car.ImageSaved(r);
        Assert.Equal(ImageState.Ok, car.CheckImage(r));
        Assert.False(car.IsNoChange(r));
        Assert.Equal(r.Sha256, Car.Open(CarFolder).Get(1)!.Sha256);

        File.Delete(r.ImagePath);
        Assert.Equal(ImageState.Missing, car.CheckImage(r));
    }

    [Fact]
    public void LockedRevisionsCantBeSaved()
    {
        var car = NewCar();
        var r = car.NewRevision(car.Revisions[0], "Base");
        var child = car.NewRevision(r, "Next");
        Assert.Equal(LockReason.HasChildren, car.LockReasonOf(r));
        Assert.Throws<InvalidOperationException>(() => car.ImageSaved(r));
        Assert.Throws<InvalidOperationException>(() => car.ImageSaved(car.Revisions[0]));
        car.ImageSaved(child);

        // Once nothing is based on it, it can be edited again.
        car.SetParent(child, car.Revisions[0]);
        Assert.Equal(LockReason.None, car.LockReasonOf(r));
        car.ImageSaved(r);
    }

    [Fact]
    public void NotesAreKeptInTheRevisionFolder()
    {
        var car = NewCar();
        var r = car.NewRevision(car.Revisions[0], "Notes");
        Assert.Equal("", car.ReadNotes(r));
        car.WriteNotes(r, "* revlimit: tour set to 7200\n");
        Assert.Equal("* revlimit: tour set to 7200\n", File.ReadAllText(Path.Combine(r.FolderPath, "notes.txt")));
        Assert.Equal("* revlimit: tour set to 7200\n", car.ReadNotes(r));
        car.WriteNotes(r, "  \n");
        Assert.False(File.Exists(r.NotesPath));
    }

    [Fact]
    public void RelabellingRenamesTheFolder()
    {
        var car = NewCar();
        var r = car.NewRevision(car.Revisions[0], "maf", new DateTime(2026, 7, 12));
        car.WriteNotes(r, "note");
        car.SetLabel(r, "MAF  for 3.5\" intake ");
        Assert.Equal("001 2026-07-12 MAF for 3.5- intake", r.Folder);
        // The label keeps what a folder name can't hold; only the folder is cleaned.
        Assert.Equal("MAF for 3.5\" intake", r.Label);
        Assert.Equal("MAF for 3.5\" intake", Car.Open(CarFolder).Get(1)!.Label);
        var colon = car.NewRevision(r, "Stage 0: knock safe", new DateTime(2026, 7, 13));
        Assert.Equal("Stage 0: knock safe", colon.Label);
        Assert.Equal("002 2026-07-13 Stage 0- knock safe", colon.Folder);
        Assert.True(File.Exists(r.ImagePath));
        Assert.Equal("note", car.ReadNotes(r));
        Assert.Equal(r.Folder, Car.Open(CarFolder).Get(1)!.Folder);

        car.SetLabel(r, "maf for 3.5- INTAKE");   // case only
        Assert.Equal("001 2026-07-12 maf for 3.5- INTAKE", r.Folder);
        Assert.True(File.Exists(r.ImagePath));
    }

    [Fact]
    public void DeletingARevisionRebasesItsChildren()
    {
        var car = NewCar();
        var a = car.NewRevision(car.Revisions[0], "a");
        var b = car.NewRevision(a, "b");
        var c1 = car.NewRevision(b, "c1");
        var c2 = car.NewRevision(b, "c2");
        var folder = b.FolderPath;

        var rebased = car.DeleteRevision(b);
        Assert.Equal([c1, c2], rebased);
        Assert.False(Directory.Exists(folder));
        Assert.Null(car.Get(2));
        Assert.Equal(1, c1.Parent);
        Assert.Equal(1, c2.Parent);
        var reopened = Car.Open(CarFolder);
        Assert.Equal([0, 1, 3, 4], reopened.Revisions.Select(r => r.Number));
        Assert.Equal(1, reopened.Get(3)!.Parent);
        Assert.Throws<InvalidOperationException>(() => car.DeleteRevision(car.Revisions[0]));
    }

    [Fact]
    public void DeletedNumbersArentReused()
    {
        var car = NewCar();
        var a = car.NewRevision(car.Revisions[0], "a");
        var b = car.NewRevision(a, "b");
        car.DeleteRevision(b);
        Assert.Equal(3, car.NewRevision(a, "c").Number);

        // Also across reopening, when the deleted one had the highest number.
        car.DeleteRevision(car.Get(3)!);
        var reopened = Car.Open(CarFolder);
        Assert.Equal(4, reopened.NewRevision(reopened.Get(1)!, "d").Number);
    }

    [Fact]
    public void AFailedFolderRemovalChangesNothing()
    {
        var car = NewCar();
        var a = car.NewRevision(car.Revisions[0], "a");
        var b = car.NewRevision(a, "b");
        Assert.Throws<IOException>(() => car.DeleteRevision(a, _ => throw new IOException("in use")));
        Assert.NotNull(car.Get(1));
        Assert.Equal(1, b.Parent);
        Assert.True(Directory.Exists(a.FolderPath));
        Assert.Equal([0, 1, 2], Car.Open(CarFolder).Revisions.Select(r => r.Number));

        string? removed = null;
        car.DeleteRevision(a, path => removed = path);
        Assert.Equal(a.FolderPath, removed);
        Assert.Equal(0, b.Parent);
    }

    [Fact]
    public void ParentsCantLoop()
    {
        var car = NewCar();
        var a = car.NewRevision(car.Revisions[0], "a");
        var b = car.NewRevision(a, "b");
        Assert.Throws<InvalidOperationException>(() => car.SetParent(a, b));
        Assert.Throws<InvalidOperationException>(() => car.SetParent(a, a));
        Assert.Throws<InvalidOperationException>(() => car.SetParent(car.Revisions[0], b));
        car.SetParent(b, car.Revisions[0]);
        Assert.Equal(0, Car.Open(CarFolder).Get(2)!.Parent);
    }

    [Fact]
    public void AttachmentsAreCopiedBesideTheImage()
    {
        var car = NewCar();
        var r = car.NewRevision(car.Revisions[0], "Logs");
        car.WriteNotes(r, "x");
        var log = Path.Combine(_root, "HighSpeed_20260725_150925.csv");
        File.WriteAllText(log, "rpm,load");
        var first = car.AddAttachment(r, log);
        var second = car.AddAttachment(r, log);
        Assert.Equal("HighSpeed_20260725_150925 (2).csv", Path.GetFileName(second));
        Assert.Equal(new[] { first, second }.Order(), car.Attachments(r).Order());
        Assert.True(File.Exists(log));
    }

    [Fact]
    public void ListsFoldersNoRevisionUses()
    {
        var car = NewCar();
        Directory.CreateDirectory(Path.Combine(CarFolder, "copied in"));
        Assert.Equal(["copied in"], car.UnindexedFolders());
    }

    [Fact]
    public void FindsTheRevisionOfAnImage()
    {
        var car = NewCar();
        var r = car.NewRevision(car.Revisions[0], "x");
        var found = TuneLibrary.FindCarOfImage(r.ImagePath);
        Assert.NotNull(found);
        Assert.Equal(1, found.FindByImagePath(r.ImagePath)!.Number);
        Assert.Null(TuneLibrary.FindCarOfImage(_stock));
        Assert.Equal([CarFolder], TuneLibrary.FindCars(Path.Combine(_root, "Library")));
        Assert.Empty(TuneLibrary.FindCars(Path.Combine(_root, "missing")));
    }

    [Theory]
    [InlineData("""{ "format": 2, "revisions": [] }""", typeof(NotSupportedException))]
    [InlineData("""{ "revisions": [] }""", typeof(InvalidDataException))]
    [InlineData("""not json""", typeof(InvalidDataException))]
    [InlineData("""{ "format": 1, "revisions": [ { "number": 0, "folder": "..", "image": "a.bin" } ] }""", typeof(InvalidDataException))]
    [InlineData("""{ "format": 1, "revisions": [ { "number": 0, "folder": "a\\b", "image": "a.bin" } ] }""", typeof(InvalidDataException))]
    [InlineData("""{ "format": 1, "revisions": [ { "number": 0, "folder": "a", "image": "..\\a.bin" } ] }""", typeof(InvalidDataException))]
    [InlineData("""{ "format": 1, "revisions": [ { "number": 0, "folder": "a", "image": "x" }, { "number": 0, "folder": "b", "image": "x" } ] }""", typeof(InvalidDataException))]
    [InlineData("""{ "format": 1, "revisions": [ { "number": 1, "folder": "a", "image": "x", "parent": 2 }, { "number": 2, "folder": "b", "image": "x", "parent": 1 } ] }""", typeof(InvalidDataException))]
    public void RefusesBadIndexes(string json, Type expected)
    {
        Directory.CreateDirectory(CarFolder);
        File.WriteAllText(Path.Combine(CarFolder, "car.json"), json);
        Assert.Throws(expected, () => Car.Open(CarFolder));
    }

    [Fact]
    public void AMissingParentMakesARoot()
    {
        Directory.CreateDirectory(CarFolder);
        File.WriteAllText(Path.Combine(CarFolder, "car.json"),
            """{ "format": 1, "revisions": [ { "number": 3, "folder": "a", "image": "x", "parent": 2 } ] }""");
        var car = Car.Open(CarFolder);
        Assert.Null(car.ParentOf(car.Revisions[0]));
        Assert.Equal("", car.Revisions[0].Label);
    }

    [Theory]
    [InlineData("  MAF   rescale ", "MAF rescale")]
    [InlineData("a/b\\c:d", "a-b-c-d")]
    [InlineData("ends with dots...", "ends with dots")]
    [InlineData("", "")]
    public void CleansLabelsForFolderNames(string label, string expected) => Assert.Equal(expected, Car.CleanLabel(label));

    [Fact]
    public void NamesFoldersAndImages()
    {
        Assert.Equal("012 2026-08-03 MAF rescale", Car.FolderName(12, new DateTime(2026, 8, 3), "MAF rescale"));
        Assert.Equal("1234 2026-08-03", Car.FolderName(1234, new DateTime(2026, 8, 3), ""));
        Assert.Equal("2011-evora-na", Car.Slug("2011 Evora NA"));
        Assert.Equal("car", Car.Slug("***"));
    }

    [Fact]
    public void TimelineNodesSkipHiddenAndRejectedRevisions()
    {
        var car = NewCar();
        var a = car.NewRevision(car.Revisions[0], "a");
        var backup = car.NewRevision(a, "backup");
        var c = car.NewRevision(backup, "c");
        var lean = car.NewRevision(a, "too lean");
        car.SetHidden(backup, true);
        car.SetRejected(lean, "too lean");

        var shown = TimelineLayout.NodesFor(car, showHidden: false);
        Assert.Equal([0, 1, 3], shown.Select(n => n.Number));
        Assert.Equal(1, shown.Single(n => n.Number == 3).Parent);   // attached past the hidden backup

        var all = TimelineLayout.NodesFor(car, showHidden: true);
        Assert.Equal(5, all.Count);
        Assert.Equal(2, all.Single(n => n.Number == c.Number).Parent);
        Assert.Contains(all, n => n.Number == lean.Number);
    }
}
