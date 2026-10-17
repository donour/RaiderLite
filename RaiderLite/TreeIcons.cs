using System.Drawing.Drawing2D;
using RaiderLite.Core.Model;

namespace RaiderLite;

/// <summary>
/// Icons for the ROM/table tree, drawn at runtime in the style of RomRaider's
/// orange tree icons: one cell for a scalar, a row for an array, a grid for a map.
/// </summary>
public static class TreeIcons
{
    public const int Rom = 0;
    public const int Category = 1;
    public const int Scalar = 2;
    public const int Array = 3;
    public const int Map = 4;
    public const int Switch = 5;
    public const int None = 6;

    private static readonly Color GradientStart = Color.FromArgb(244, 92, 40);
    private static readonly Color GradientEnd = Color.FromArgb(255, 222, 40);
    private static readonly Color Outline = Color.FromArgb(40, 30, 20);

    public static int For(Table table) => table.Shape switch
    {
        TableShape.Scalar => Scalar,
        TableShape.Array => Array,
        TableShape.Map => Map,
        _ => Switch,
    };

    /// <summary>Builds the image list, scaled for <paramref name="dpi"/>.</summary>
    public static ImageList Create(int dpi)
    {
        int size = Math.Max(16, (int)Math.Round(16 * dpi / 96.0));
        var list = new ImageList { ImageSize = new Size(size, size), ColorDepth = ColorDepth.Depth32Bit };
        list.Images.Add(Draw(size, DrawRom));
        list.Images.Add(Draw(size, DrawFolder));
        list.Images.Add(Draw(size, (g, s) => DrawCells(g, s, 1, 1)));
        list.Images.Add(Draw(size, (g, s) => DrawCells(g, s, 4, 1)));
        list.Images.Add(Draw(size, (g, s) => DrawCells(g, s, 3, 3)));
        list.Images.Add(Draw(size, DrawSwitch));
        list.Images.Add(Draw(size, (_, _) => { }));
        return list;
    }

    private static Bitmap Draw(int size, Action<Graphics, float> paint)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.PixelOffsetMode = PixelOffsetMode.Half;
        paint(g, size / 16f);
        return bmp;
    }

    private static Brush Gradient(RectangleF r) =>
        new LinearGradientBrush(new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Top), GradientStart, GradientEnd);

    /// <summary>A block of cells: columns x rows, centred, sized to read at 16px.</summary>
    private static void DrawCells(Graphics g, float s, int columns, int rows)
    {
        // Cell pitch in 16px units, chosen so each shape fills a similar area.
        float pitch = columns == 1 && rows == 1 ? 9 : columns >= 4 ? 3.5f : 4.5f;
        // Single-row arrays get taller cells so the strip doesn't read as a line.
        float rowPitch = rows == 1 && columns > 1 ? 6 : pitch;
        float w = pitch * columns, h = rowPitch * rows;
        float x0 = (16 - w) / 2f, y0 = (16 - h) / 2f;
        var outer = new RectangleF(x0 * s, y0 * s, w * s, h * s);
        using (var fill = Gradient(outer))
            g.FillRectangle(fill, outer);
        using var pen = new Pen(Outline, Math.Max(1f, s));
        for (int c = 0; c <= columns; c++)
        {
            float x = (x0 + c * pitch) * s;
            g.DrawLine(pen, x, outer.Top, x, outer.Bottom);
        }
        for (int r = 0; r <= rows; r++)
        {
            float y = (y0 + r * rowPitch) * s;
            g.DrawLine(pen, outer.Left, y, outer.Right, y);
        }
    }

    private static void DrawSwitch(Graphics g, float s)
    {
        // A toggle: rounded track with the knob on the right.
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(1 * s, 4.5f * s, 14 * s, 7 * s);
        using var path = new GraphicsPath();
        float d = track.Height;
        path.AddArc(track.Left, track.Top, d, d, 90, 180);
        path.AddArc(track.Right - d, track.Top, d, d, 270, 180);
        path.CloseFigure();
        using (var fill = Gradient(track))
            g.FillPath(fill, path);
        using var pen = new Pen(Outline, Math.Max(1f, s));
        g.DrawPath(pen, path);
        var knob = new RectangleF(track.Right - d + 1.2f * s, track.Top + 1.2f * s, d - 2.4f * s, d - 2.4f * s);
        g.FillEllipse(Brushes.White, knob);
        g.DrawEllipse(pen, knob);
    }

    private static void DrawFolder(Graphics g, float s)
    {
        using var back = new SolidBrush(Color.FromArgb(222, 178, 80));
        using var front = new SolidBrush(Color.FromArgb(246, 208, 110));
        using var pen = new Pen(Color.FromArgb(150, 110, 40), Math.Max(1f, s));
        var tab = new RectangleF(1.5f * s, 3 * s, 6 * s, 3 * s);
        var body = new RectangleF(1.5f * s, 4.5f * s, 13 * s, 9 * s);
        g.FillRectangle(back, tab);
        g.DrawRectangle(pen, tab.X, tab.Y, tab.Width, tab.Height);
        g.FillRectangle(front, body);
        g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
    }

    private static void DrawRom(Graphics g, float s)
    {
        // A chip with pins.
        using var bodyBrush = new SolidBrush(Color.FromArgb(60, 64, 72));
        using var pin = new Pen(Color.FromArgb(150, 150, 150), Math.Max(1f, s));
        for (int i = 0; i < 4; i++)
        {
            float y = (4 + i * 2.7f) * s;
            g.DrawLine(pin, 1 * s, y, 3.5f * s, y);
            g.DrawLine(pin, 12.5f * s, y, 15 * s, y);
        }
        var body = new RectangleF(3.5f * s, 2.5f * s, 9 * s, 11 * s);
        g.FillRectangle(bodyBrush, body);
        using var notch = new SolidBrush(Color.FromArgb(120, 124, 132));
        g.FillEllipse(notch, 5 * s, 4 * s, 2 * s, 2 * s);
    }
}
