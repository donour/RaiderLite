using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RaiderLite.Core.Model;

namespace RaiderLite.Core.Tunes;

/// <summary>
/// One ECU's calibration history: a folder holding one subfolder per <see cref="Revision"/> and an
/// index, <c>car.json</c>, with what file names can't hold (parents, hashes, rejections...). Every
/// change is saved immediately.
/// </summary>
public sealed class Car
{
    public const string IndexFileName = "car.json";
    public const string NotesFileName = "notes.txt";

    /// <summary>The index format this version writes; newer indexes are refused.</summary>
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly List<Revision> _revisions;
    // Numbers aren't reused once a revision is deleted: the next one after every number given out.
    private int _nextNumber;

    private Car(string folder, string name, string definition, List<Revision> revisions)
    {
        Folder = folder;
        Name = name;
        Definition = definition;
        _revisions = revisions;
        foreach (var r in _revisions)
            r.Car = this;
        _revisions.Sort((a, b) => a.Number.CompareTo(b.Number));
    }

    public string Folder { get; }

    public string Name { get; private set; }

    /// <summary>The XML id of the definition the car's images use.</summary>
    public string Definition { get; private set; }

    /// <summary>Every revision, by number.</summary>
    public IReadOnlyList<Revision> Revisions => _revisions;

    public string IndexPath => Path.Combine(Folder, IndexFileName);

    public static bool IsCar(string folder) => File.Exists(Path.Combine(folder, IndexFileName));

    // ----- opening and creating -----

    /// <summary>Opens the car in <paramref name="folder"/>.</summary>
    /// <exception cref="InvalidDataException">The index is damaged or inconsistent.</exception>
    /// <exception cref="NotSupportedException">The index was written by a newer RaiderLite.</exception>
    public static Car Open(string folder)
    {
        folder = Path.GetFullPath(folder);
        var path = Path.Combine(folder, IndexFileName);
        CarIndex index;
        try
        {
            index = JsonSerializer.Deserialize<CarIndex>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"{path} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} can't be read: {ex.Message}", ex);
        }
        if (index.Format < 1)
            throw new InvalidDataException($"{path} has no format number.");
        if (index.Format > FormatVersion)
            throw new NotSupportedException($"{path} was written by a newer version of RaiderLite (format {index.Format}).");
        var revisions = index.Revisions ?? [];
        Validate(path, revisions);
        return new Car(folder, index.Name ?? Path.GetFileName(folder), index.Definition ?? "", revisions) { _nextNumber = index.NextNumber ?? 0 };
    }

    /// <summary>
    /// Creates a car in <paramref name="folder"/> (which must not exist or be empty) whose revision
    /// 000 is a copy of <paramref name="stockImagePath"/>, keeping its file name and date.
    /// </summary>
    public static Car Create(string folder, string name, string definition, string stockImagePath)
    {
        folder = Path.GetFullPath(folder);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new IOException($"{folder} is not empty.");
        Directory.CreateDirectory(folder);
        var car = new Car(folder, name.Trim(), definition, []);
        var created = File.GetLastWriteTime(stockImagePath);
        car.AddRevision(null, "Stock", created, File.ReadAllBytes(stockImagePath), Path.GetFileName(stockImagePath));
        car.Save();
        return car;
    }

    /// <summary>
    /// Adds a revision holding <paramref name="image"/> (written as <paramref name="imageName"/>)
    /// in a new folder, without saving the index: the caller saves once it has finished.
    /// </summary>
    internal Revision AddRevision(Revision? parent, string label, DateTime created, byte[] image, string imageName)
    {
        int number = NextNumber();
        _nextNumber = number + 1;
        created = TruncateToSeconds(created);
        label = TidyLabel(label);
        var folder = UniqueFolderName(FolderName(number, created, label));
        Directory.CreateDirectory(Path.Combine(Folder, folder));
        var revision = new Revision
        {
            Car = this,
            Number = number,
            Folder = folder,
            Image = imageName,
            Created = created,
            Parent = parent?.Number,
            Label = label,
        };
        SafeFile.WriteAllBytes(revision.ImagePath, image);
        revision.Sha256 = Hash(image);
        _revisions.Add(revision);
        return revision;
    }

    // ----- structure -----

    public Revision? Get(int number) => _revisions.Find(r => r.Number == number);

    public Revision? ParentOf(Revision revision) => revision.Parent is int p ? Get(p) : null;

    public IReadOnlyList<Revision> ChildrenOf(Revision revision) => _revisions.Where(r => r.Parent == revision.Number).ToList();

    /// <summary>The revision and its ancestors, newest first.</summary>
    public IReadOnlyList<Revision> Lineage(Revision revision)
    {
        var lineage = new List<Revision>();
        for (Revision? r = revision; r != null && !lineage.Contains(r); r = ParentOf(r))
            lineage.Add(r);
        return lineage;
    }

    /// <summary>
    /// Stock and revisions others are based on can't be edited in place, so a revision's children
    /// always show what changed from what it really was.
    /// </summary>
    public LockReason LockReasonOf(Revision revision)
    {
        if (revision.Number == 0)
            return LockReason.Stock;
        if (_revisions.Any(r => r.Parent == revision.Number))
            return LockReason.HasChildren;
        return LockReason.None;
    }

    public bool IsLocked(Revision revision) => LockReasonOf(revision) != LockReason.None;

    /// <summary>A duplicate of its parent: same image, kept for its own notes, files and date.</summary>
    public bool IsNoChange(Revision revision) =>
        ParentOf(revision) is { } parent && string.Equals(parent.Sha256, revision.Sha256, StringComparison.OrdinalIgnoreCase);

    // ----- changes -----

    /// <summary>
    /// Creates a revision based on <paramref name="parent"/>: a new folder with a copy of the
    /// parent's image, named <c>yyyyMMdd-car-number</c> with the parent image's extension.
    /// </summary>
    public Revision NewRevision(Revision parent, string label, DateTime? created = null)
    {
        CheckOwn(parent);
        var when = created ?? DateTime.Now;
        int number = NextNumber();
        var imageName = $"{when:yyyyMMdd}-{Slug(Name)}-{number:000}{Path.GetExtension(parent.Image)}";
        var revision = AddRevision(parent, label, when, File.ReadAllBytes(parent.ImagePath), imageName);
        Save();
        return revision;
    }

    public string ReadNotes(Revision revision)
    {
        CheckOwn(revision);
        return File.Exists(revision.NotesPath) ? File.ReadAllText(revision.NotesPath) : "";
    }

    /// <summary>Writes the revision's notes; empty notes remove the file.</summary>
    public void WriteNotes(Revision revision, string text)
    {
        CheckOwn(revision);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (File.Exists(revision.NotesPath))
                File.Delete(revision.NotesPath);
            return;
        }
        SafeFile.WriteAllText(revision.NotesPath, text);
    }

    /// <summary>Changes the label and renames the folder to match (kept as it is if that fails).</summary>
    public void SetLabel(Revision revision, string label)
    {
        CheckOwn(revision);
        revision.Label = TidyLabel(label);
        var wanted = FolderName(revision.Number, revision.Created, revision.Label);
        if (!string.Equals(wanted, revision.Folder, StringComparison.Ordinal))
        {
            try
            {
                // A case-only rename goes through a temporary name: Windows sees the same folder.
                var target = string.Equals(wanted, revision.Folder, StringComparison.OrdinalIgnoreCase) ? wanted : UniqueFolderName(wanted);
                var temp = Path.Combine(Folder, $"{revision.Folder}.{Guid.NewGuid():N}");
                Directory.Move(revision.FolderPath, temp);
                Directory.Move(temp, Path.Combine(Folder, target));
                revision.Folder = target;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file in the folder is open elsewhere; the label alone changes.
            }
        }
        Save();
    }

    /// <summary>Sets the revision it was based on; refused for stock and when it would make a loop.</summary>
    public void SetParent(Revision revision, Revision? parent)
    {
        CheckOwn(revision);
        if (parent != null)
        {
            CheckOwn(parent);
            if (revision.Number == 0)
                throw new InvalidOperationException("Stock can't be based on another revision.");
            if (Lineage(parent).Contains(revision))
                throw new InvalidOperationException($"{revision} can't be based on its own descendant {parent}.");
        }
        revision.Parent = parent?.Number;
        Save();
    }

    /// <summary>Rejects the revision with a reason, or clears the rejection with null.</summary>
    public void SetRejected(Revision revision, string? reason)
    {
        CheckOwn(revision);
        revision.Rejected = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Save();
    }

    public void SetHidden(Revision revision, bool hidden)
    {
        CheckOwn(revision);
        revision.Hidden = hidden;
        Save();
    }

    /// <summary>
    /// Deletes a revision: <paramref name="removeFolder"/> removes its folder (the editor sends it
    /// to the Recycle Bin; by default it is deleted), then the revisions based on it are based on
    /// its parent instead. Stock can't be deleted, and its number isn't given out again. If removing
    /// the folder fails, nothing changes. Returns the revisions that were re-based.
    /// </summary>
    public IReadOnlyList<Revision> DeleteRevision(Revision revision, Action<string>? removeFolder = null)
    {
        CheckOwn(revision);
        if (revision.Number == 0)
            throw new InvalidOperationException("Stock can't be deleted.");
        var children = ChildrenOf(revision);
        if (Directory.Exists(revision.FolderPath))
            (removeFolder ?? (path => Directory.Delete(path, recursive: true)))(revision.FolderPath);
        _nextNumber = NextNumber();
        foreach (var child in children)
            child.Parent = revision.Parent;
        _revisions.Remove(revision);
        Save();
        return children;
    }

    private int NextNumber() => Math.Max(_revisions.Count == 0 ? 0 : _revisions.Max(r => r.Number) + 1, _nextNumber);

    // ----- the image -----

    public ImageState CheckImage(Revision revision)
    {
        CheckOwn(revision);
        if (!File.Exists(revision.ImagePath))
            return ImageState.Missing;
        return string.Equals(Hash(File.ReadAllBytes(revision.ImagePath)), revision.Sha256, StringComparison.OrdinalIgnoreCase)
            ? ImageState.Ok
            : ImageState.Changed;
    }

    /// <summary>Records the hash of the image the editor just saved. Locked revisions must not be saved.</summary>
    public void ImageSaved(Revision revision)
    {
        CheckOwn(revision);
        if (IsLocked(revision))
            throw new InvalidOperationException($"{revision} is locked ({LockReasonOf(revision)}).");
        revision.Sha256 = Hash(File.ReadAllBytes(revision.ImagePath));
        Save();
    }

    /// <summary>The revision whose image is <paramref name="path"/>, if it belongs to this car.</summary>
    public Revision? FindByImagePath(string path)
    {
        var full = Path.GetFullPath(path);
        return _revisions.Find(r => string.Equals(Path.GetFullPath(r.ImagePath), full, StringComparison.OrdinalIgnoreCase));
    }

    // ----- files -----

    /// <summary>Files in the revision's folder other than its image and notes.</summary>
    public IReadOnlyList<string> Attachments(Revision revision)
    {
        CheckOwn(revision);
        if (!Directory.Exists(revision.FolderPath))
            return [];
        return Directory.EnumerateFiles(revision.FolderPath)
            .Where(f => !IsOwnFile(revision, f))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Copies a file into the revision's folder, renaming it if the name is taken; returns the new path.</summary>
    public string AddAttachment(Revision revision, string sourcePath)
    {
        CheckOwn(revision);
        var name = Path.GetFileName(sourcePath);
        var target = Path.Combine(revision.FolderPath, name);
        for (int i = 2; File.Exists(target) || IsOwnFile(revision, target); i++)
            target = Path.Combine(revision.FolderPath, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
        File.Copy(sourcePath, target);
        return target;
    }

    private static bool IsOwnFile(Revision revision, string path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(name, revision.Image, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, NotesFileName, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Subfolders of the car that no revision uses (added or restored outside RaiderLite).</summary>
    public IReadOnlyList<string> UnindexedFolders()
    {
        var used = new HashSet<string>(_revisions.Select(r => r.Folder), StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateDirectories(Folder)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(n => !used.Contains(n))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ----- saving -----

    /// <summary>Writes the index (atomically).</summary>
    public void Save()
    {
        var index = new CarIndex
        {
            Format = FormatVersion,
            Name = Name,
            Definition = Definition,
            // Only needed once the highest number was deleted.
            NextNumber = _nextNumber > (_revisions.Count == 0 ? 0 : _revisions.Max(r => r.Number) + 1) ? _nextNumber : null,
            Revisions = _revisions,
        };
        SafeFile.WriteAllText(IndexPath, JsonSerializer.Serialize(index, JsonOptions) + Environment.NewLine);
    }

    // ----- helpers -----

    public static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>"012 2026-08-03 MAF rescale": the number keeps folders in order; the rest is for people.</summary>
    public static string FolderName(int number, DateTime created, string label)
    {
        var name = $"{number:000} {created:yyyy-MM-dd}";
        var clean = CleanLabel(label);
        if (clean.Length > 0)
            name += " " + clean;
        return name;
    }

    /// <summary>A label as stored: trimmed, single spaces, one line. Only folder names need <see cref="CleanLabel"/>.</summary>
    public static string TidyLabel(string label) =>
        string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A label usable in a folder name: no path characters, single spaces, at most 60 characters.</summary>
    public static string CleanLabel(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(label.Length);
        foreach (var c in label)
            sb.Append(invalid.Contains(c) ? '-' : char.IsWhiteSpace(c) ? ' ' : c);
        var clean = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > 60)
            clean = clean[..60];
        // Windows drops trailing dots and spaces from folder names.
        return clean.TrimEnd('.', ' ');
    }

    /// <summary>"2011 Evora NA" -> "2011-evora-na", for image file names.</summary>
    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "car" : slug;
    }

    private string UniqueFolderName(string wanted)
    {
        var name = wanted;
        for (int i = 2; Directory.Exists(Path.Combine(Folder, name)) || File.Exists(Path.Combine(Folder, name)); i++)
            name = $"{wanted} ({i})";
        return name;
    }

    private static DateTime TruncateToSeconds(DateTime t) =>
        new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);

    private void CheckOwn(Revision revision)
    {
        if (!ReferenceEquals(revision.Car, this) || !_revisions.Contains(revision))
            throw new ArgumentException($"{revision} is not a revision of {Name}.", nameof(revision));
    }

    /// <summary>Refuses indexes whose paths leave the car's folder, duplicate numbers or folders, or parent loops.</summary>
    private static void Validate(string path, List<Revision> revisions)
    {
        // JSON null entries deserialize as null despite the element type.
        if (revisions.Any(r => r is null))
            throw new InvalidDataException($"{path} has an empty entry in its revision list.");
        static bool IsPlainName(string? name) =>
            !string.IsNullOrWhiteSpace(name) && name != "." && name != ".."
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !Path.IsPathRooted(name);

        var numbers = new HashSet<int>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in revisions)
        {
            if (!numbers.Add(r.Number))
                throw new InvalidDataException($"{path}: revision {r.Number} appears twice.");
            if (!IsPlainName(r.Folder) || !folders.Add(r.Folder))
                throw new InvalidDataException($"{path}: revision {r.Number} has a bad or duplicate folder \"{r.Folder}\".");
            if (!IsPlainName(r.Image))
                throw new InvalidDataException($"{path}: revision {r.Number} has a bad image name \"{r.Image}\".");
            r.Label ??= "";
            r.Sha256 ??= "";
        }
        var byNumber = revisions.ToDictionary(r => r.Number);
        foreach (var r in revisions)
        {
            var seen = new HashSet<int>();
            for (int? n = r.Number; n is int k && byNumber.TryGetValue(k, out var current); n = current.Parent)
            {
                if (!seen.Add(k))
                    throw new InvalidDataException($"{path}: revision {r.Number}'s parents form a loop.");
            }
        }
    }

    private sealed class CarIndex
    {
        public int Format { get; set; }
        public string? Name { get; set; }
        public string? Definition { get; set; }
        public int? NextNumber { get; set; }
        public List<Revision>? Revisions { get; set; }
    }
}
