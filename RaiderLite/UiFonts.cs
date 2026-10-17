namespace RaiderLite;

/// <summary>
/// Fonts shared by every window. Controls don't dispose fonts assigned to them, so creating
/// a new Font per window, label update or cell paint just churns GDI handles.
/// </summary>
public static class UiFonts
{
    public static readonly Font Regular = new("Segoe UI", 9f);
    public static readonly Font Bold = new("Segoe UI", 9f, FontStyle.Bold);
    public static readonly Font Heading = new("Segoe UI", 11f, FontStyle.Bold);
    public static readonly Font Small = new("Segoe UI", 8f);
    public static readonly Font Value = new("Segoe UI", 10f);
}
