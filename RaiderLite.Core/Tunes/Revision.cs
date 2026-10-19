using System.Text.Json.Serialization;

namespace RaiderLite.Core.Tunes;

/// <summary>Why a revision can't be edited in place.</summary>
public enum LockReason { None, Stock, HasChildren }

/// <summary>Whether a revision's image is still the one RaiderLite last saved or imported.</summary>
public enum ImageState { Ok, Missing, Changed }

/// <summary>
/// One complete image of a car, with its notes and files, in its own folder of the car. Revisions
/// are changed through their <see cref="Car"/>, which saves its index after every change.
/// </summary>
public sealed class Revision
{
    [JsonConstructor]
    internal Revision()
    {
    }

    /// <summary>Unique within the car, in order of creation; 0 is stock.</summary>
    [JsonInclude]
    public int Number { get; internal set; }

    /// <summary>The revision's folder name, inside the car's folder.</summary>
    [JsonInclude]
    public string Folder { get; internal set; } = "";

    /// <summary>The image's file name, inside <see cref="Folder"/>.</summary>
    [JsonInclude]
    public string Image { get; internal set; } = "";

    /// <summary>SHA-256 of the image as last saved or imported (lower-case hex).</summary>
    [JsonInclude]
    public string Sha256 { get; internal set; } = "";

    [JsonInclude]
    public DateTime Created { get; internal set; }

    /// <summary>The number of the revision this one was based on; null for stock.</summary>
    [JsonInclude]
    public int? Parent { get; internal set; }

    [JsonInclude]
    public string Label { get; internal set; } = "";

    /// <summary>The reason it was rejected ("too lean"), or null.</summary>
    [JsonInclude]
    public string? Rejected { get; internal set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Hidden { get; internal set; }

    /// <summary>Where an imported revision came from, relative to the imported folder.</summary>
    [JsonInclude]
    public string? ImportedFrom { get; internal set; }

    [JsonIgnore]
    public Car Car { get; internal set; } = null!;

    [JsonIgnore]
    public string FolderPath => Path.Combine(Car.Folder, Folder);

    [JsonIgnore]
    public string ImagePath => Path.Combine(FolderPath, Image);

    [JsonIgnore]
    public string NotesPath => Path.Combine(FolderPath, Car.NotesFileName);

    public override string ToString() => string.IsNullOrEmpty(Label) ? $"{Number:000}" : $"{Number:000} {Label}";
}
