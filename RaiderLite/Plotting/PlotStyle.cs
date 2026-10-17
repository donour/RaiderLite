using RaiderLite.Core.Model;
using RaiderLite.Core.Plotting;

namespace RaiderLite.Plotting;

/// <summary>Colours and sizes shared by the graph views.</summary>
internal static class PlotStyle
{
    public static readonly Color Background = Color.White;
    public static readonly Color Frame = Color.FromArgb(190, 194, 200);
    public static readonly Color GridLine = Color.FromArgb(234, 236, 239);
    public static readonly Color Label = Color.FromArgb(90, 94, 102);
    public static readonly Color Title = Color.FromArgb(50, 54, 62);
    public static readonly Color Curve = Color.FromArgb(0, 103, 192);
    public static readonly Color Original = Color.FromArgb(150, 150, 150);
    public static readonly Color Compared = Color.FromArgb(214, 120, 0);
    public static readonly Color Crosshair = Color.FromArgb(150, 150, 160);
    public static readonly Color ReadoutBack = Color.FromArgb(245, 255, 255, 255);

    public static Color Selection => AppSettings.Current.HighlightColor;

    /// <summary>The grid's min→max colour at <paramref name="t"/> (0..1).</summary>
    public static Color Heat(double t) =>
        ColorScaler.GetScaledColor(t, AppSettings.Current.MinColor, AppSettings.Current.MaxColor);

    /// <summary>A darker heat colour that reads well as a line on white.</summary>
    public static Color HeatLine(double t)
    {
        var c = Heat(t);
        return Color.FromArgb((int)(c.R * 0.72), (int)(c.G * 0.72), (int)(c.B * 0.72));
    }

    public static Color WithAlpha(Color c, int alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}

/// <summary>A point under the mouse: the cell and the text describing it.</summary>
internal sealed record PlotHit(DataCell Cell, PointF Location, string Readout);

/// <summary>Formats values the way the grid does.</summary>
internal static class PlotText
{
    public static string Value(GraphData data, double v) => ValueFormatter.Format(v, data.ValueFormat);

    public static string Tick(double v, double step) => ValueFormatter.Format(v, AxisTicks.FormatFor(step));

    /// <summary>"label units" for a hover readout, e.g. "2.50 V".</summary>
    public static string Readout(GraphData data, int i, int j)
    {
        var parts = new List<string> { $"{data.X.Title}: {data.X.Labels[i]}" };
        if (data.Y != null)
            parts.Add($"{data.Y.Title}: {data.Y.Labels[j]}");
        var cell = data.Cells[i, j];
        parts.Add($"{Value(data, data.Values[i, j])} {data.ValueTitle}   (raw {cell.BinValue.ToString(System.Globalization.CultureInfo.CurrentCulture)})");
        return string.Join("\n", parts);
    }
}
