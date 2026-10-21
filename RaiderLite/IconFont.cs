using System.Drawing.Text;

namespace RaiderLite;

/// <summary>
/// Renders icons from the Windows icon font (Segoe Fluent Icons on Windows 11,
/// Segoe MDL2 Assets on Windows 10), so they stay sharp at any DPI.
/// </summary>
public static class IconFont
{
    public const char Open = '\uE838';
    public const char Save = '\uE74E';
    public const char SaveAs = '\uE792';
    public const char Close = '\uE711';
    public const char Undo = '\uE7A7';
    public const char Redo = '\uE7A6';
    public const char Revert = '\uE777';
    public const char Copy = '\uE8C8';
    public const char Paste = '\uE77F';
    public const char Plus = '\uE948';
    public const char Minus = '\uE949';
    public const char Multiply = '\uE947';
    public const char Accept = '\uE73E';
    public const char Interpolate = '\uE9D2';
    public const char Vertical = '\uE8CB';
    public const char Horizontal = '\uE8AB';
    public const char History = '\uE81C';
    public const char SelectAll = '\uE8B3';
    public const char Info = '\uE946';
    public const char Library = '\uE8F1';
    public const char Settings = '\uE713';
    public const char Compare = '\uE7C4';
    public const char Search = '\uE1A3';
    public const char Sliders = '\uE9E9';
    public const char Chart = '\uE9F9';
    public const char Surface = '\uF158';
    public const char Rescale = '\uE740';
    public const char Lock = '\uE72E';
    public const char NewRevision = '\uE8F4';
    public const char Folder = '\uE8B7';
    public const char More = '\uE712';
    public const char Back = '\uE72B';
    public const char Forward = '\uE72A';
    public const char Help = '\uE897';

    private static readonly string? FamilyName = FindFamily();
    private static readonly Dictionary<(char, int, int), Bitmap> Cache = [];

    public static bool Available => FamilyName != null;

    private static string? FindFamily()
    {
        using var fonts = new InstalledFontCollection();
        var names = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }.FirstOrDefault(names.Contains);
    }

    /// <summary>The glyph drawn in <paramref name="color"/> on a transparent square of <paramref name="size"/> pixels.</summary>
    public static Bitmap Render(char glyph, int size, Color color)
    {
        var key = (glyph, size, color.ToArgb());
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            if (FamilyName != null)
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                // Icon fonts are designed on an em square; a slightly smaller em leaves breathing room.
                using var font = new Font(FamilyName, size * 0.78f, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(color);
                using var format = new StringFormat(StringFormat.GenericTypographic)
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, size, size), format);
            }
            else
            {
                // No icon font: a plain rounded square keeps the layout intact.
                using var brush = new SolidBrush(color);
                g.FillRectangle(brush, size / 4, size / 4, size / 2, size / 2);
            }
        }
        Cache[key] = bmp;
        return bmp;
    }
}
