namespace RaiderLite.Core.Tunes;

/// <summary>A folder of cars: each subfolder with a <c>car.json</c> is one.</summary>
public static class TuneLibrary
{
    /// <summary>The car folders in <paramref name="libraryFolder"/>, by name; none if it doesn't exist.</summary>
    public static IReadOnlyList<string> FindCars(string libraryFolder)
    {
        if (!Directory.Exists(libraryFolder))
            return [];
        return Directory.EnumerateDirectories(libraryFolder)
            .Where(Car.IsCar)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The car whose folder contains <paramref name="imagePath"/> as a revision image, if any.</summary>
    public static Car? FindCarOfImage(string imagePath)
    {
        // An image sits in <car>\<revision folder>\<image>.
        var carFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(imagePath)));
        if (carFolder == null || !Car.IsCar(carFolder))
            return null;
        try
        {
            var car = Car.Open(carFolder);
            return car.FindByImagePath(imagePath) != null ? car : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
