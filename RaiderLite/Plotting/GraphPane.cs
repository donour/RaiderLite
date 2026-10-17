using RaiderLite.Core.Model;
using RaiderLite.Core.Plotting;

namespace RaiderLite.Plotting;

/// <summary>
/// The graph area of a table window: a <see cref="PlotControl"/>, with a slim toolbar for maps
/// (Lines / Surface, Transpose, Reset View). Right-click copies the graph as an image.
/// </summary>
public sealed class GraphPane : Panel
{
    private readonly Table _table;
    private readonly ToolStrip _bar;
    private readonly ToolStripButton _linesButton;
    private readonly ToolStripButton _surfaceButton;
    private readonly ToolStripButton _transposeButton;
    private readonly ToolStripButton _resetButton;
    private readonly ContextMenuStrip _menu = new();

    public GraphPane(Table table)
    {
        _table = table;
        BackColor = PlotStyle.Background;
        Plot.Dock = DockStyle.Fill;
        Controls.Add(Plot);

        _bar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            BackColor = PlotStyle.Background,
            Padding = new Padding(4, 0, 4, 0),
            CanOverflow = false,
        };
        float dpi = DeviceDpi / 96f;
        ToolStripButton Button(string text, char glyph, string tip, Action click)
        {
            var b = new ToolStripButton(text)
            {
                Image = IconFont.Render(glyph, (int)Math.Round(16 * dpi), Color.FromArgb(50, 54, 62)),
                ImageScaling = ToolStripItemImageScaling.None,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                ToolTipText = tip,
                AutoToolTip = false,
            };
            b.Click += (_, _) => click();
            _bar.Items.Add(b);
            return b;
        }
        _linesButton = Button("Lines", IconFont.Interpolate, "One line per row of the map", () => SetSurface(false));
        _surfaceButton = Button("Surface", IconFont.Surface, "3D surface (drag to rotate)", () => SetSurface(true));
        _bar.Items.Add(new ToolStripSeparator());
        _transposeButton = Button("Transpose", IconFont.Horizontal, "Lines per column instead of per row", () =>
        {
            Plot.Transposed = !Plot.Transposed;
            UpdateButtons();
        });
        _resetButton = Button("Reset View", IconFont.Revert, "Return the surface to its default angle", () =>
        {
            Plot.Camera.Reset();
            Plot.Invalidate();
        });

        // Only maps have view choices; curves show just the graph.
        if (table is Table3D)
        {
            Controls.Add(_bar);
            Plot.SurfaceMode = AppSettings.Current.MapGraphSurface;
        }

        _menu.Items.Add("&Copy Image", null, (_, _) => CopyImage());
        Plot.ContextMenuStrip = _menu;
        RefreshData();
    }

    public PlotControl Plot { get; } = new();

    private void SetSurface(bool surface)
    {
        Plot.SurfaceMode = surface;
        // The last choice is used for maps opened later.
        AppSettings.Current.MapGraphSurface = surface;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        // A map with a single row or column can't be a surface: it stays as lines.
        bool surface = Plot.ShowingSurface;
        _surfaceButton.Enabled = Plot.SurfaceAvailable;
        _linesButton.Checked = !surface;
        _surfaceButton.Checked = surface;
        _transposeButton.Checked = Plot.Transposed;
        _transposeButton.Enabled = !surface;
        _resetButton.Enabled = surface;
    }

    /// <summary>Re-reads the table (after its values, scaling or compare state changed).</summary>
    public void RefreshData()
    {
        Plot.SetData(GraphData.From(_table));
        UpdateButtons();
    }

    public void CopyImage()
    {
        using var bmp = Plot.RenderToBitmap(Plot.ClientSize);
        try
        {
            Clipboard.SetImage(bmp);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard is busy; nothing else to do.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menu.Dispose();
            _bar.Dispose(); // not in Controls for curves
        }
        base.Dispose(disposing);
    }
}
