using System.Drawing.Text;

namespace RaiderLite;

/// <summary>
/// Renders icons from the Windows icon font (Segoe Fluent Icons on Windows 11,
/// Segoe MDL2 Assets on Windows 10), so they stay sharp at any DPI.
/// </summary>
public static class IconFont
{
    public const char Open = '';
    public const char Save = '';
    public const char SaveAs = '';
    public const char Close = '';
    public const char Undo = '';
    public const char Redo = '';
    public const char Revert = '';
    public const char Copy = '';
    public const char Paste = '';
    public const char Plus = '';
    public const char Minus = '';
    public const char Multiply = '';
    public const char Accept = '';
    public const char Interpolate = '';
    public const char Vertical = '';
    public const char Horizontal = '';
    public const char History = '';
    public const char SelectAll = '';
    public const char Info = '';
    public const char Library = '';
    public const char Settings = '';
    public const char Compare = '';
    public const char Search = '';
    public const char Sliders = '';

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
