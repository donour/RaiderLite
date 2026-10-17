using System.Text.Json;
using System.Text.Json.Serialization;
using RaiderLite.Core.Model;

namespace RaiderLite;

/// <summary>User settings, stored as JSON in %APPDATA%\RaiderLite\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new ColorJsonConverter() },
    };

    private static readonly string DefaultSettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RaiderLite", "settings.json");

    /// <summary>Settings file; RAIDERLITE_SETTINGS overrides the default location.</summary>
    public static string SettingsPath { get; } = ResolveSettingsPath();

    /// <summary>Folder of the settings file (also where the error log goes).</summary>
    public static string SettingsDirectory => Path.GetDirectoryName(SettingsPath) ?? Path.GetTempPath();

    private static string ResolveSettingsPath()
    {
        if (Environment.GetEnvironmentVariable("RAIDERLITE_SETTINGS") is not { Length: > 0 } custom)
            return DefaultSettingsPath;
        try
        {
            // A bare file name would have no directory to create; make it absolute.
            return Path.GetFullPath(custom);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return DefaultSettingsPath;
        }
    }

    public static AppSettings Current { get; private set; } = new();

    // Files and folders
    public List<string> DefinitionFiles { get; set; } = [];
    public List<string> RecentFiles { get; set; } = [];
    public string LastImageDir { get; set; } = "";
    public string LastDefinitionDir { get; set; } = "";

    // Editing
    public int UserLevel { get; set; } = 1;
    public bool DisplayHighTables { get; set; } = true;
    public bool SaveDebugTables { get; set; } = true;
    public string DefaultScale { get; set; } = "Metric";
    public bool ScaleHeadersAndData { get; set; } = true;
    public bool ValueLimitWarning { get; set; } = true;

    // Table tree
    public bool SortTableTree { get; set; }
    /// <summary>Click a table in the tree to open it, click again to close it (RomRaider's default).</summary>
    public bool SingleClickTables { get; set; } = true;

    // Graphs
    /// <summary>Open table windows with their graph shown (graphs start hidden by default).</summary>
    public bool ShowGraphsByDefault { get; set; }

    /// <summary>Last graph style chosen for maps: 3D surface (true) or lines.</summary>
    public bool MapGraphSurface { get; set; }

    // Table display
    public int CellWidth { get; set; } = 48;
    public int CellHeight { get; set; } = 20;
    public bool ColorAxis { get; set; }
    public Color MaxColor { get; set; } = Color.FromArgb(255, 102, 102);
    public Color MinColor { get; set; } = Color.FromArgb(153, 153, 255);
    public Color HighlightColor { get; set; } = Color.FromArgb(27, 161, 226);
    public Color IncreaseBorder { get; set; } = Color.FromArgb(255, 0, 0);
    public Color DecreaseBorder { get; set; } = Color.FromArgb(0, 0, 255);
    public Color AxisColor { get; set; } = Color.FromArgb(255, 255, 255);
    public Color WarningColor { get; set; } = Color.FromArgb(255, 0, 0);

    // Window state
    /// <summary>Table window bounds by "definition xmlid|table name", as "x,y,width,height".</summary>
    public Dictionary<string, string> TableWindows { get; set; } = [];

    public Rectangle WindowBounds { get; set; }
    public bool WindowMaximized { get; set; }
    public int TreeWidth { get; set; } = 320;

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Current = new AppSettings();
        }
        Current.ApplyToCore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save them must not break the editor.
        }
    }

    /// <summary>Pushes the options the model consults into <see cref="CoreSettings"/>.</summary>
    public void ApplyToCore()
    {
        CoreSettings.Current.UserLevel = UserLevel;
        CoreSettings.Current.SaveDebugTables = SaveDebugTables;
        CoreSettings.Current.DefaultScale = DefaultScale;
        CoreSettings.Current.ScaleHeadersAndData = ScaleHeadersAndData;
    }

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10)
            RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
    }

    private const int MaxRememberedTableWindows = 400;

    public Rectangle? GetTableWindow(string key)
    {
        if (!TableWindows.TryGetValue(key, out var text))
            return null;
        var parts = text.Split(',');
        if (parts.Length == 4 && parts.All(p => int.TryParse(p, out _)))
        {
            var v = parts.Select(int.Parse).ToArray();
            if (v[2] > 0 && v[3] > 0)
                return new Rectangle(v[0], v[1], v[2], v[3]);
        }
        return null;
    }

    public void SetTableWindow(string key, Rectangle bounds)
    {
        TableWindows.Remove(key);
        TableWindows[key] = $"{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}";
        // Keep the file small: drop the oldest entries (insertion order) beyond the cap.
        while (TableWindows.Count > MaxRememberedTableWindows)
            TableWindows.Remove(TableWindows.Keys.First());
    }

    public static readonly string[] UserLevelNames = ["Beginner", "Intermediate", "Advanced", "All", "Debug"];

    private sealed class ColorJsonConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            return string.IsNullOrEmpty(s) ? Color.Empty : ColorTranslator.FromHtml(s);
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"#{value.R:X2}{value.G:X2}{value.B:X2}");
    }
}
