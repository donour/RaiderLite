using System.Globalization;
using RaiderLite.Core.Definitions;
using RaiderLite.Core.Model;
using RaiderLite.Core.Tunes;

namespace RaiderLite;

/// <summary>
/// One <see cref="Car"/> per car folder for the whole app, so the editor (saving images) and the
/// Tunes panel (notes, labels) change and save the same index instead of overwriting each other.
/// </summary>
internal sealed class CarRegistry
{
    private readonly Dictionary<string, Car> _cars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The car in <paramref name="folder"/>, opened once.</summary>
    /// <exception cref="InvalidDataException">The index is damaged.</exception>
    /// <exception cref="NotSupportedException">The index is from a newer RaiderLite.</exception>
    public Car Open(string folder)
    {
        var key = Key(folder);
        if (!_cars.TryGetValue(key, out var car))
            _cars[key] = car = Car.Open(key);
        return car;
    }

    /// <summary>Drops the cached car, so the next <see cref="Open"/> rereads its index.</summary>
    public void Forget(string folder) => _cars.Remove(Key(folder));

    /// <summary>The revision whose image is <paramref name="imagePath"/>, if it is one.</summary>
    public Revision? FindRevision(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath))
            return null;
        try
        {
            // A revision's image sits in <car>\<revision folder>\<image>.
            var carFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(imagePath)));
            if (carFolder == null || !Car.IsCar(carFolder))
                return null;
            return Open(carFolder).FindByImagePath(imagePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static string Key(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}

/// <summary>What changed in a revision against its parent, or why that isn't known.</summary>
internal sealed record RevisionChanges(ImageComparison? Comparison, string? Problem);

/// <summary>
/// A car shown in the Tunes panel, with what each revision changed and its notes (kept for
/// searching). The changes need the car's definition, loaded once as a template, and are worked
/// out off the UI thread.
/// </summary>
internal sealed class CarSession(Car car)
{
    private Dictionary<int, RevisionChanges> _changes = [];
    private readonly Dictionary<int, string> _notes = car.Revisions.ToDictionary(r => r.Number, ReadNotes);
    private int _generation;

    public Car Car { get; } = car;

    /// <summary>The car's definition loaded with its stock image, used to decode every revision.</summary>
    public Rom? Template { get; private set; }

    /// <summary>Why changes can't be shown at all (no definition, unreadable stock image...).</summary>
    public string? Problem { get; private set; }

    public bool Loaded { get; private set; }

    public RevisionChanges? ChangesOf(Revision revision) => _changes.GetValueOrDefault(revision.Number);

    /// <summary>A revision's notes as last read; "" if they couldn't be read.</summary>
    public string NotesOf(Revision revision) => _notes.GetValueOrDefault(revision.Number, "");

    /// <summary>Reads a revision's notes again, after they were saved or the revision was added.</summary>
    public void ReloadNotes(Revision revision)
    {
        if (ReferenceEquals(revision.Car, Car))
            _notes[revision.Number] = ReadNotes(revision);
    }

    /// <summary>A revision's notes from its folder; "" if they can't be read.</summary>
    public static string ReadNotes(Revision revision)
    {
        try
        {
            return revision.Car.ReadNotes(revision);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>
    /// Whether <paramref name="word"/> is in a revision's label, number, folder, rejection reason,
    /// origin or notes, or names a table it changed.
    /// </summary>
    public bool Matches(Revision revision, string word)
    {
        bool Has(string? s) => s != null && s.Contains(word, StringComparison.CurrentCultureIgnoreCase);
        return Has(revision.Label) || Has(revision.Number.ToString("000", CultureInfo.InvariantCulture)) || Has(revision.Folder)
               || Has(revision.Rejected) || Has(revision.ImportedFrom) || Has(NotesOf(revision))
               || ChangesOf(revision)?.Comparison?.Tables.Any(t => Has(t.Table.Name)) == true;
    }

    /// <summary>
    /// Works out every revision's changes (loading the template first if needed). Returns false
    /// if a newer call superseded this one.
    /// </summary>
    public async Task<bool> RefreshAsync(Func<string, (DefinitionDocument Doc, RomDefinitionInfo Info)?> findDefinition)
    {
        int generation = ++_generation;
        var revisions = Car.Revisions.ToList();
        var parents = revisions.ToDictionary(r => r.Number, r => Car.ParentOf(r));
        var template = Template;
        var (newTemplate, problem, changes) = await Task.Run(() =>
        {
            string? templateProblem = null;
            if (template == null)
                (template, templateProblem) = LoadTemplate(revisions, findDefinition);

            var bytes = new Dictionary<int, byte[]?>();
            byte[]? Read(Revision r)
            {
                if (!bytes.TryGetValue(r.Number, out var b))
                {
                    try
                    {
                        b = File.ReadAllBytes(r.ImagePath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        b = null;
                    }
                    bytes[r.Number] = b;
                }
                return b;
            }

            var result = new Dictionary<int, RevisionChanges>();
            if (template != null)
            {
                foreach (var r in revisions)
                {
                    if (parents[r.Number] is not { } parent)
                        continue;
                    result[r.Number] = Compare(template, Read(parent), Read(r));
                }
            }
            return (template, templateProblem, result);
        });
        if (generation != _generation)
            return false;
        Template = newTemplate;
        Problem = newTemplate != null ? null : problem ?? Problem;
        _changes = changes;
        Loaded = true;
        return true;
    }

    /// <summary>Compares two revisions now (they are small); null without a template.</summary>
    public RevisionChanges? Compare(Revision first, Revision second)
    {
        if (Template == null)
            return null;
        byte[]? Read(Revision r)
        {
            try
            {
                return File.ReadAllBytes(r.ImagePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        return Compare(Template, Read(first), Read(second));
    }

    private static RevisionChanges Compare(Rom template, byte[]? first, byte[]? second)
    {
        if (first == null || second == null)
            return new RevisionChanges(null, "An image is missing or can't be read.");
        var comparison = ImageDiff.Compare(template, first, second);
        return comparison.SameSize
            ? new RevisionChanges(comparison, null)
            : new RevisionChanges(null, "The images differ in size, so they can't be compared table by table.");
    }

    private static (Rom?, string?) LoadTemplate(IReadOnlyList<Revision> revisions,
        Func<string, (DefinitionDocument Doc, RomDefinitionInfo Info)?> findDefinition)
    {
        var car = revisions.Count > 0 ? revisions[0].Car : null;
        if (car == null)
            return (null, null);
        if (findDefinition(car.Definition) is not { } definition)
            return (null, $"The definition {car.Definition} isn't in the definition list (Edit > Definition Manager), so changes can't be shown.");
        foreach (var r in revisions)
        {
            try
            {
                var image = File.ReadAllBytes(r.ImagePath);
                return (RomUnmarshaller.Load(definition.Doc, definition.Info.Node, image, r.ImagePath), null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try the next revision's image.
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return (null, $"{car.Definition} can't be loaded with {r.Image}: {ex.Message}");
            }
        }
        return (null, "None of the car's images can be read.");
    }
}
