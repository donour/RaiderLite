using RaiderLite.Core.Model;
using RaiderLite.Core.Plotting;
using RaiderLite.Plotting;

namespace RaiderLite;

/// <summary>MDI child window that shows and edits one table.</summary>
public sealed class TableForm : Form
{
    private readonly MainForm _main;
    private readonly TableGrid? _grid;
    private readonly ScalarEditor? _scalar;
    // Child controls are positioned by LayoutContent rather than docked: see there for the rules.
    private const int ContentPadding = 4;
    private readonly ContentPanel _content;
    private readonly Label _titleLabel = new() { AutoSize = false, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0), AutoEllipsis = true };
    private readonly Label _xLabel = new() { AutoSize = false, Height = 18, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true };
    private readonly VerticalLabel _yLabel = new() { Width = 20 };
    private readonly Label _valueLabel = new() { AutoSize = false, Height = 18, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly FlowLayoutPanel _presetPanel = new() { AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(2, 4, 6, 4) };
    private bool _layingOut;

    // One tooltip for the whole window, disposed with it. A ToolTip owns a native window that
    // keeps it (and every control it references) alive until it is disposed.
    private readonly ToolTip _tips = new() { AutoPopDelay = 30000 };

    // Graph pane: below the grid for curves, to its right for maps. Created when first shown.
    private const int GraphGap = 6;
    private GraphPane? _graph;
    private bool _graphVisible;
    private Size? _sizeBeforeGraph;
    private readonly ToolStripMenuItem _graphMenuItem = new("&Graph") { ShortcutKeyDisplayString = "Ctrl+G" };
    private readonly List<(ButtonBase Button, PresetEntry Entry)> _presetButtons = [];
    private readonly Label? _rawBytesLabel;
    private readonly ToolStripMenuItem _compareOff = new("&Off");
    private readonly ToolStripMenuItem _compareOriginal = new("Show &Changes (vs. original)");
    private readonly ToolStripMenuItem _compareRomMenu = new("Compare to &ROM");
    private readonly ToolStripMenuItem _displayAbsolute = new("&Absolute difference");
    private readonly ToolStripMenuItem _displayPercent = new("&Percent difference");
    private readonly ToolStripMenuItem _compareToCurrent = new("Compare to &current values");
    private readonly ToolStripMenuItem _compareToOriginalValues = new("Compare to &original values");
    private Table? _comparedTable;
    private bool _refreshPending;

    public TableForm(MainForm main, Table table)
    {
        _main = main;
        Table = table;
        _content = new ContentPanel(LayoutContent);
        // No layout passes while the window is being filled.
        _content.SuspendLayout();
        Text = $"{table.Name} - {(table.Rom is { } rom ? main.ImageName(rom) : "")}";
        ShowIcon = false;
        KeyPreview = false;
        // Maximized MDI children hide their caption, and with their own (unmerged) menu bar the
        // restore button has nowhere to go. Table windows fit their table instead: double-click
        // the title bar (see WndProc).
        MaximizeBox = false;
        Font = UiFonts.Regular;
        BackColor = SystemColors.Window;

        bool isSwitch = table is TableSwitch;
        if (table.Shape == TableShape.Scalar && !isSwitch)
        {
            // One value: a compact in-place editor, no grid or menu bar.
            _scalar = new ScalarEditor(this, table) { Dock = DockStyle.Fill };
            Controls.Add(_scalar);
            ContextMenuStrip = BuildScalarMenu();
            _scalar.ContextMenuStrip = ContextMenuStrip;
            // A small fixed tool window: caption with the table name and a close button.
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Text = table.Name;
            table.DataChanged += OnTableDataChanged;
            table.ScaleChanged += OnScaleChanged;
            return;
        }

        var menu = BuildMenu();
        if (!isSwitch)
        {
            _grid = new TableGrid(table);
            _grid.Command += (_, c) => RunCommand(c);
            _grid.ValueEntryStarted += (_, ch) =>
            {
                // Typing a value into a locked revision asks about a new revision straight away.
                if (Table.Rom?.ReadOnlyReason != null)
                    _main.OnEditBlocked(Table.Rom);
                else
                    _main.Toolbar.BeginValueEntry(ch);
            };
            _grid.CurrentCellChanged += (_, _) => _main.UpdateCellStatus(this);
            _grid.SelectionChanged += (_, _) =>
            {
                _main.UpdateCellStatus(this);
                _graph?.Plot.RefreshSelection();
            };
        }
        else
        {
            _rawBytesLabel = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 3) };
        }

        if (!isSwitch)
            _content.Controls.Add(_titleLabel);
        if (table is Table3D or Table2D)
            _content.Controls.Add(_xLabel);
        if (table is Table3D)
            _content.Controls.Add(_yLabel);
        if (_grid != null)
        {
            _content.Controls.Add(_grid);
            _content.Controls.Add(_valueLabel);
        }
        if (table.Presets.Presets.Count > 0 || isSwitch)
        {
            BuildPresetPanel(isSwitch);
            _content.Controls.Add(_presetPanel);
        }
        // The description is kept out of the window (it took too much room); hover the title
        // to read it, or see Table Properties.
        if (DescriptionTip(table) is { } tip)
            _tips.SetToolTip(_titleLabel, tip);
        _content.ResumeLayout(false);
        if (GraphAvailable && AppSettings.Current.ShowGraphsByDefault)
            ShowGraph(true, resize: false);

        // Fill first, menu last: the menu docks above the content.
        Controls.Add(_content);
        Controls.Add(menu);
        MainMenuStrip = menu;
        MinimumSize = new Size(240, 180);

        HookScaleLabel(_valueLabel, () => Table);
        HookScaleLabel(_xLabel, () => Table switch { Table3D t3 => t3.XAxis, Table2D t2 => t2.Axis, _ => null });
        HookScaleLabel(_yLabel, () => (Table as Table3D)?.YAxis);

        UpdateLabels();
        UpdatePresetButtons();

        table.DataChanged += OnTableDataChanged;
        table.ScaleChanged += OnScaleChanged;
    }

    public Table Table { get; }

    /// <summary>The help page for F1: about the graph, a switch, a single value or editing.</summary>
    public string HelpTopic => _graph?.ContainsFocus == true ? "graphs"
        : Table is TableSwitch ? "table-windows#switches"
        : _scalar != null ? "table-windows#single-values"
        : "editing";

    /// <summary>When the window was created (to tell a double-click from two single clicks).</summary>
    public DateTime OpenedAt { get; } = DateTime.UtcNow;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.G) && GraphAvailable)
        {
            ToggleGraph();
            return true;
        }
        // Esc closes the table, unless it is cancelling a value being typed.
        if (keyData == Keys.Escape && !(_scalar?.IsEditing ?? false))
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public TableGrid? Grid => _grid;

    /// <summary>Right-click menu of the compact scalar window (it has no menu bar).</summary>
    private ContextMenuStrip BuildScalarMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("&Undo", null, (_, _) => RunCommand(GridCommand.Undo));
        menu.Items.Add("&Redo", null, (_, _) => RunCommand(GridCommand.Redo));
        menu.Items.Add("Revert to &Original", null, (_, _) => RunEdit("Revert", t => t.UndoAll()));
        menu.Items.Add("Set Revert &Point", null, (_, _) => Table.SetRevertPoint());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("&Copy Value", null, (_, _) => SetClipboard(ValueFormatter.ToClipboardString(Table.DataCells.First().RealValue)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Table &Properties...", null, (_, _) => _main.ShowTableProperties(Table));
        menu.Items.Add("&Close", null, (_, _) => Close());
        return menu;
    }

    public Rom? Rom => Table.Rom;

    /// <summary>Whether cells show their change from the original values.</summary>
    public bool ShowingChanges => ReferenceEquals(_comparedTable, Table);

    public void ToggleShowChanges() => SetCompare(ShowingChanges ? null : Table);

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { AllowMerge = false, Dock = DockStyle.Top, RenderMode = ToolStripRenderMode.System };

        var tableMenu = new ToolStripMenuItem("&Table");
        tableMenu.DropDownItems.Add("Table &Properties...", null, (_, _) => _main.ShowTableProperties(Table));
        tableMenu.DropDownItems.Add(new ToolStripSeparator());
        tableMenu.DropDownItems.Add("&Close", null, (_, _) => Close());

        var editMenu = new ToolStripMenuItem("&Edit");
        editMenu.DropDownItems.Add(new ToolStripMenuItem("&Undo", null, (_, _) => RunCommand(GridCommand.Undo), Keys.Control | Keys.Z) { ShortcutKeyDisplayString = "Ctrl+Z" });
        editMenu.DropDownItems.Add(new ToolStripMenuItem("&Redo", null, (_, _) => RunCommand(GridCommand.Redo)) { ShortcutKeyDisplayString = "Ctrl+Y" });
        editMenu.DropDownItems.Add(new ToolStripSeparator());
        editMenu.DropDownItems.Add("Undo &Selected Changes", null, (_, _) => RunEdit("Undo selected changes", t => t.UndoSelected()));
        editMenu.DropDownItems.Add("Undo &All Changes", null, (_, _) => RunEdit("Undo all changes", t => t.UndoAll()));
        editMenu.DropDownItems.Add("Set Revert &Point", null, (_, _) => Table.SetRevertPoint());
        editMenu.DropDownItems.Add(new ToolStripSeparator());
        editMenu.DropDownItems.Add(new ToolStripMenuItem("&Copy Selection", null, (_, _) => CopySelection()) { ShortcutKeyDisplayString = "Ctrl+C" });
        editMenu.DropDownItems.Add("Copy &Table", null, (_, _) => CopyTable());
        editMenu.DropDownItems.Add(new ToolStripMenuItem("&Paste", null, (_, _) => Paste()) { ShortcutKeyDisplayString = "Ctrl+V" });
        editMenu.DropDownItems.Add(new ToolStripSeparator());
        editMenu.DropDownItems.Add(new ToolStripMenuItem("&Interpolate", null, (_, _) => RunCommand(GridCommand.Interpolate)) { ShortcutKeyDisplayString = "Shift+I" });
        editMenu.DropDownItems.Add(new ToolStripMenuItem("&Vertical Interpolate", null, (_, _) => RunCommand(GridCommand.VerticalInterpolate)) { ShortcutKeyDisplayString = "Shift+V" });
        editMenu.DropDownItems.Add(new ToolStripMenuItem("&Horizontal Interpolate", null, (_, _) => RunCommand(GridCommand.HorizontalInterpolate)) { ShortcutKeyDisplayString = "Shift+H" });
        editMenu.DropDownItems.Add(new ToolStripMenuItem("Rescale A&xis...", null, (_, _) => RescaleAxis()) { Enabled = CanRescaleAxis });
        editMenu.DropDownItems.Add(new ToolStripSeparator());
        editMenu.DropDownItems.Add(new ToolStripMenuItem("Select All &Data", null, (_, _) => _grid?.SelectAllData()));

        var compareMenu = new ToolStripMenuItem("&Compare");
        _compareOff.Click += (_, _) => SetCompare(null);
        _compareOriginal.Click += (_, _) => CompareToOriginal();
        compareMenu.DropDownItems.AddRange([_compareOff, _compareOriginal, _compareRomMenu, new ToolStripSeparator(),
            _displayAbsolute, _displayPercent, new ToolStripSeparator(), _compareToCurrent, _compareToOriginalValues]);
        compareMenu.DropDownOpening += (_, _) => UpdateCompareMenu();
        _displayAbsolute.Click += (_, _) => SetCompareDisplay(CompareDisplay.Absolute);
        _displayPercent.Click += (_, _) => SetCompareDisplay(CompareDisplay.Percent);
        _compareToCurrent.Click += (_, _) => SetCompareValueType(CompareValueType.Bin);
        _compareToOriginalValues.Click += (_, _) => SetCompareValueType(CompareValueType.Original);

        var viewMenu = new ToolStripMenuItem("&View");
        _graphMenuItem.Click += (_, _) => ToggleGraph();
        viewMenu.DropDownOpening += (_, _) =>
        {
            _graphMenuItem.Checked = _graphVisible;
            _graphMenuItem.Enabled = GraphAvailable;
        };
        viewMenu.DropDownItems.Add(_graphMenuItem);
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Fit Window to Table", null, (_, _) => ToggleFit()) { ShortcutKeyDisplayString = "double-click title" });

        if (Table is TableSwitch)
            menu.Items.AddRange([tableMenu, viewMenu]);
        else
            menu.Items.AddRange([tableMenu, editMenu, compareMenu, viewMenu]);
        return menu;
    }

    // ----- layout -----

    private void UpdateLabels()
    {
        var scale = Table.CurrentScale;
        string units = scale.IsRaw ? "raw" : scale.Unit;
        _titleLabel.Text = Table.Name;
        _titleLabel.Font = UiFonts.Bold;
        _valueLabel.Text = Table.CompareTable == null
            ? $"Values: {units} ▾" + (Table.ReadOnlyReason is { } readOnly ? $"   ({readOnly})" : "")
            : $"Comparing to {DescribeCompare()} - {(_grid?.CompareDisplay == CompareDisplay.Percent ? "percent" : "absolute")} difference";

        switch (Table)
        {
            case Table3D t3:
                _xLabel.Text = AxisCaption(t3.XAxis);
                _yLabel.Text = AxisCaption(t3.YAxis);
                break;
            case Table2D t2:
                _xLabel.Text = AxisCaption(t2.Axis);
                break;
        }
    }

    private static string AxisCaption(Table1D axis)
    {
        var scale = axis.CurrentScale;
        if (axis.IsStaticDataTable)
            return axis.Name;
        string units = scale.IsRaw ? "raw" : scale.Unit;
        return (string.IsNullOrEmpty(axis.Name) ? units : $"{axis.Name} ({units})") + " ▾";
    }

    /// <summary>Makes a caption open a menu of the scalings of <paramref name="target"/> when clicked.</summary>
    private void HookScaleLabel(Control label, Func<Table?> target)
    {
        _tips.SetToolTip(label, "Click to change the scaling");
        label.Cursor = Cursors.Hand;
        label.MouseClick += (_, e) =>
        {
            var t = target();
            if (t != null && e.Button == MouseButtons.Left)
                ShowScaleMenu(t, label, e.Location);
        };
    }

    internal static void ShowScaleMenu(Table table, Control anchor, Point location)
    {
        if (table.IsStaticDataTable || table.Scales.Count < 2)
            return;
        var menu = new ContextMenuStrip();
        foreach (var scale in table.Scales)
        {
            var s = scale;
            var item = new ToolStripMenuItem(ScaleText(s)) { Checked = ReferenceEquals(s, table.CurrentScale) };
            item.Click += (_, _) => table.SetCurrentScale(s);
            menu.Items.Add(item);
        }
        MainForm.ShowAndDispose(menu, anchor, location);
    }

    /// <summary>"Metric (°C)" style text for a scaling; "Raw value" for raw.</summary>
    public static string ScaleText(Scale s) => s.IsRaw ? "Raw value" : $"{s.Category} ({s.Unit})";

    private string DescribeCompare()
    {
        if (_comparedTable == null)
            return "";
        if (ReferenceEquals(_comparedTable, Table))
            return "original values";
        return _comparedTable.Rom is { } rom ? _main.ImageName(rom) : _comparedTable.Name;
    }

    /// <summary>Resizes the window to show the whole table, within the MDI client area.</summary>
    public void SizeToContent()
    {
        FitClientSize();
        KeepInView();
    }

    private void FitClientSize() => ClientSize = FittedClientSize();

    /// <summary>The client size that shows the whole table, within the MDI client area.</summary>
    private Size FittedClientSize()
    {
        if (_scalar != null)
        {
            // Wide enough to read the table name in the caption.
            var caption = TextRenderer.MeasureText(Text, SystemFonts.SmallCaptionFont ?? Font).Width + 48;
            var content = _scalar.ContentSize;
            return new Size(Math.Min(Math.Max(content.Width, caption), 420), content.Height);
        }

        var max = _main.MdiClientArea;
        int maxWidth = Math.Max(300, max.Width - 20);
        int maxHeight = Math.Max(150, max.Height - 20);
        int width = Math.Min(NaturalClientWidth(), maxWidth);
        int height = Math.Min(RequiredClientHeight(width), maxHeight);
        // If the height had to be limited the grid scrolls vertically: make room for that scrollbar.
        if (_grid != null && height < RequiredClientHeight(width))
            width = Math.Min(width + SystemInformation.VerticalScrollBarWidth, maxWidth);
        return new Size(width, height);
    }

    /// <summary>The window size that shows the whole table (what fitting would give), without resizing.</summary>
    public Size FittedWindowSize() => FittedClientSize() + (Size - ClientSize);

    /// <summary>Single values have a fixed-size window: arranging moves it but keeps its size.</summary>
    public bool FixedSize => _scalar != null;

    /// <summary>Client width that shows the whole table (and the menu bar) without scrolling.</summary>
    private int NaturalClientWidth()
    {
        const int pad = ContentPadding;
        var presets = _presetPanel.Parent != null ? _presetPanel.GetPreferredSize(new Size(600, 0)) : Size.Empty;
        int width = _grid != null
            ? Math.Max(_grid.ContentSize.Width + (_yLabel.Parent != null ? _yLabel.Width : 0), presets.Width)
            : presets.Width;
        width = Math.Max(width + 2 * pad, Table is TableSwitch ? 320 : 240);
        if (_graphVisible)
            width = GraphBelowWhenFitted ? Math.Max(width, Px(480)) : width + GraphGap + Px(440);
        return Math.Max(width, (MainMenuStrip?.GetPreferredSize(Size.Empty).Width ?? 0) + 12);
    }

    /// <summary>
    /// Client height that shows every row at <paramref name="clientWidth"/>, including the
    /// horizontal scrollbar the grid gets when the table is wider than that.
    /// </summary>
    private int RequiredClientHeight(int clientWidth)
    {
        const int pad = ContentPadding;
        int height = _titleLabel.Parent != null ? _titleLabel.Height : 0;
        var presets = _presetPanel.Parent != null ? _presetPanel.GetPreferredSize(new Size(Math.Max(100, clientWidth - 2 * pad), 0)) : Size.Empty;
        if (_grid != null)
        {
            var grid = _grid.ContentSize;
            int yw = _yLabel.Parent != null ? _yLabel.Width : 0;
            height += (_xLabel.Parent != null ? _xLabel.Height : 0) + grid.Height + _valueLabel.Height + presets.Height;
            if (grid.Width > clientWidth - pad - yw)
                height += SystemInformation.HorizontalScrollBarHeight;
        }
        else
        {
            height += presets.Height;
        }
        if (_graphVisible)
        {
            int title = _titleLabel.Parent != null ? _titleLabel.Height : 0;
            height = GraphBelowWhenFitted ? height + GraphGap + Px(260) : Math.Max(height, title + Px(320));
        }
        return height + pad + (MainMenuStrip?.Height ?? 0);
    }

    private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    // ----- graph -----

    /// <summary>Curves and maps have graphs; scalars and switches don't.</summary>
    public bool GraphAvailable => _grid != null && GraphData.CanGraph(Table);

    public bool GraphVisible => _graphVisible;

    /// <summary>
    /// Where the graph goes in a content area of <paramref name="w"/> × <paramref name="h"/>.
    /// Curves are wide strips, so below. Maps go right or below, whichever gives the graph more
    /// usable room; when neither gives enough, the graph goes right and the grid is limited to
    /// part of the width (it scrolls, with its axes frozen) so the graph stays readable.
    /// </summary>
    private (bool Below, int GridMaxWidth) PlaceGraph(int w, int h)
    {
        if (Table is not Table3D || _grid == null)
            return (true, int.MaxValue);
        const int pad = ContentPadding;
        var natural = _grid.ContentSize;
        int title = _titleLabel.Parent != null ? _titleLabel.Height : 0;
        int gridW = natural.Width + (_yLabel.Parent != null ? _yLabel.Width : 0) + 2 * pad;
        int gridH = title + (_xLabel.Parent != null ? _xLabel.Height : 0) + natural.Height + _valueLabel.Height + pad;
        int right = Math.Min(w - gridW - GraphGap, h - title - pad);
        int below = Math.Min(w - 2 * pad, h - gridH - GraphGap);
        if (Math.Max(right, below) >= Px(300))
            return (below > right, int.MaxValue);
        return (false, (int)(w * 0.55));
    }

    /// <summary>Placement used when fitting the window: decided by the space the editor area offers.</summary>
    private bool GraphBelowWhenFitted
    {
        get
        {
            var area = _main.MdiClientArea;
            int chrome = MainMenuStrip?.Height ?? 0;
            return PlaceGraph(Math.Max(300, area.Width - 20), Math.Max(150, area.Height - 20) - chrome - 40).Below;
        }
    }

    public void ToggleGraph() => ShowGraph(!_graphVisible, resize: true);

    private void ShowGraph(bool show, bool resize)
    {
        if (show == _graphVisible || (show && !GraphAvailable))
            return;
        _graphVisible = show;
        if (show && _graph == null)
        {
            _graph = new GraphPane(Table);
            _graph.Plot.CellClicked += (_, cell) => SelectCell(cell);
            _content.Controls.Add(_graph);
        }
        if (_graph != null)
            _graph.Visible = show;

        if (resize && WindowState == FormWindowState.Normal)
        {
            if (show)
            {
                // Grow the window enough for the graph; remember the size to return to.
                _sizeBeforeGraph = ClientSize;
                var max = _main.MdiClientArea;
                int width = Math.Max(ClientSize.Width, Math.Min(NaturalClientWidth(), Math.Max(300, max.Width - 20)));
                int height = Math.Max(ClientSize.Height, Math.Min(RequiredClientHeight(width), Math.Max(150, max.Height - 20)));
                ClientSize = new Size(width, height);
            }
            else if (_sizeBeforeGraph is { } before)
            {
                ClientSize = before;
            }
            if (!show)
                _sizeBeforeGraph = null;
            KeepInView();
        }
        _content.PerformLayout();
        if (_main.ActiveMdiChild == this)
            _main.Toolbar.Bind(this);
    }

    /// <summary>Selects a cell in the grid (when its point is clicked in the graph).</summary>
    private void SelectCell(DataCell cell)
    {
        if (_grid?.FindCell(cell) is not { } pos)
            return;
        _grid.ClearSelection();
        _grid.CurrentCell = _grid.Rows[pos.Y].Cells[pos.X];
        _grid.Focus();
    }

    /// <summary>The table's description wrapped for a tooltip, or null if it has none.</summary>
    public static string? DescriptionTip(Table table)
    {
        var text = table.Description.Trim();
        if (text.Length == 0)
            return null;
        if (text.Length > 1500)
            text = text[..1500] + "\u2026";
        // Tooltips don't wrap on their own; break long lines at about 90 characters.
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = new System.Text.StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length > 0 && line.Length + word.Length > 90)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                if (line.Length > 0)
                    line.Append(' ');
                line.Append(word);
            }
            lines.Add(line.ToString());
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Positions the window's contents top-down like a document:
    /// the table (grid with its axis captions and "Values" line) stays at its natural size,
    /// anchored top-left, so extra space is left empty instead of stretching cells or pushing
    /// captions away; when space is short the grid scrolls, with its axes frozen.
    /// </summary>
    private void LayoutContent()
    {
        if (_layingOut)
            return;
        _layingOut = true;
        try
        {
            const int pad = ContentPadding;
            int w = _content.ClientSize.Width, h = _content.ClientSize.Height;
            if (w <= 0 || h <= 0)
                return;
            int inner = Math.Max(0, w - 2 * pad);
            int y = 0;

            if (_titleLabel.Parent != null)
            {
                _titleLabel.SetBounds(0, 0, w, _titleLabel.Height);
                y = _titleLabel.Height;
            }

            int presetsPreferred = _presetPanel.Parent != null ? _presetPanel.GetPreferredSize(new Size(inner, 0)).Height : 0;

            if (_grid != null)
            {
                bool hasX = _xLabel.Parent != null, hasY = _yLabel.Parent != null;
                int xh = hasX ? _xLabel.Height : 0;
                int yw = hasY ? _yLabel.Width : 0;
                int vh = _valueLabel.Height;
                var natural = _grid.ContentSize;
                var graph = _graphVisible ? _graph : null;
                var (below, gridMaxWidth) = graph != null ? PlaceGraph(w, h) : (true, int.MaxValue);
                // The grid keeps priority, but leaves the graph a usable minimum.
                int graphRight = graph != null && !below ? GraphGap + Px(260) : 0;
                int graphBelow = graph != null && below ? GraphGap + Px(140) : 0;
                int blockTop = y;
                int availW = Math.Max(40, Math.Min(w - pad - yw - graphRight - (graphRight > 0 ? pad : 0), gridMaxWidth - pad - yw));
                int availH = Math.Max(40, h - y - xh - vh - presetsPreferred - pad - graphBelow);

                int gw = natural.Width, gh = natural.Height;
                bool vScroll = gh > availH, hScroll = gw > availW;
                if (vScroll)
                    gw += SystemInformation.VerticalScrollBarWidth;
                if (!hScroll && gw > availW)
                    hScroll = true;
                if (hScroll)
                    gh += SystemInformation.HorizontalScrollBarHeight;
                if (!vScroll && gh > availH)
                    gw += SystemInformation.VerticalScrollBarWidth;
                gw = Math.Min(gw, availW);
                gh = Math.Min(gh, availH);

                if (hasX)
                    _xLabel.SetBounds(pad + yw, y, gw, xh);
                y += xh;
                if (hasY)
                    _yLabel.SetBounds(pad, y, yw, gh);
                _grid.SetBounds(pad + yw, y, gw, gh);
                y += gh;
                int blockWidth = graphRight > 0 ? yw + gw : inner;
                _valueLabel.SetBounds(pad, y, blockWidth, vh);
                y += vh;
                if (presetsPreferred > 0)
                {
                    _presetPanel.SetBounds(pad, y, blockWidth, presetsPreferred);
                    y += presetsPreferred;
                }

                if (graph != null)
                {
                    // The graph takes whatever space is left: graphs benefit from room, unlike the grid.
                    if (below)
                    {
                        int gy = y + GraphGap;
                        graph.SetBounds(pad, gy, inner, Math.Max(Px(60), h - gy - pad));
                    }
                    else
                    {
                        int gx = pad + yw + gw + GraphGap;
                        graph.SetBounds(gx, blockTop, Math.Max(Px(120), w - gx - pad), Math.Max(Px(120), h - blockTop - pad));
                    }
                }
            }
            else if (_presetPanel.Parent != null)
            {
                // Switch: the states come first and scroll if the window is too short.
                int ph = Math.Max(Math.Min(presetsPreferred, h - y - pad), Math.Min(presetsPreferred, 60));
                _presetPanel.SetBounds(pad, y, inner, ph);
                y += ph;
            }
        }
        finally
        {
            _layingOut = false;
        }
    }

    /// <summary>A panel that leaves child placement to its owner instead of docking/anchoring.</summary>
    private sealed class ContentPanel : Panel
    {
        private readonly Action _layout;

        public ContentPanel(Action layout)
        {
            _layout = layout;
            DoubleBuffered = true;
            Dock = DockStyle.Fill;
            BackColor = SystemColors.Window;
        }

        protected override void OnLayout(LayoutEventArgs levent) => _layout();
    }

    private void BuildPresetPanel(bool isSwitch)
    {
        _presetPanel.Controls.Clear();
        _presetButtons.Clear();
        var header = new Label
        {
            Text = isSwitch ? Table.Name : "Presets",
            AutoSize = true,
            Font = isSwitch ? UiFonts.Heading : UiFonts.Bold,
            Margin = new Padding(3, 3, 3, 6),
        };
        if (isSwitch && DescriptionTip(Table) is { } descriptionTip)
            _tips.SetToolTip(header, descriptionTip);
        _presetPanel.Controls.Add(header);

        bool bitwise = Table is TableBitwiseSwitch;
        foreach (var entry in Table.Presets.Presets)
        {
            ButtonBase button = isSwitch && !bitwise
                ? new RadioButton { AutoCheck = false }
                : new CheckBox { AutoCheck = false };
            button.Text = entry.Name;
            button.AutoSize = true;
            button.Margin = new Padding(6, 1, 3, 1);
            var tip = bitwise ? $"Bit {((TableBitwiseSwitch)Table).Bits[entry.DataCellOffset]}" : $"Data: {entry.DataText}";
            _tips.SetToolTip(button, tip);
            button.Click += (_, _) => OnPresetClicked(button, entry);
            _presetButtons.Add((button, entry));
            _presetPanel.Controls.Add(button);
        }

        if (_rawBytesLabel != null)
            _presetPanel.Controls.Add(_rawBytesLabel);
    }

    private void OnPresetClicked(ButtonBase button, PresetEntry entry)
    {
        bool isChecked = button is CheckBox cb ? cb.Checked : ((RadioButton)button).Checked;
        RunEdit(entry.IsBitMask && isChecked ? $"Clear {entry.Name}" : $"Set {entry.Name}", t =>
        {
            if (!isChecked)
                PresetManager.Apply(t, entry);
            else if (entry.IsBitMask)
                PresetManager.Clear(t, entry);
        });
        UpdatePresetButtons();
    }

    private void UpdatePresetButtons()
    {
        foreach (var (button, entry) in _presetButtons)
        {
            bool active = PresetManager.IsActive(Table, entry);
            if (button is CheckBox cb)
                cb.Checked = active;
            else if (button is RadioButton rb)
                rb.Checked = active;
            button.Enabled = !Table.Locked && !Table.IsChecksumFixTable;
        }

        if (_rawBytesLabel != null && Table.Rom != null && Table.Cells.Length > 0)
        {
            int address = Table.Cells[0].Address;
            int length = Table is TableBitwiseSwitch ? StorageType.ByteLength(Table.StorageType) : Table.Cells.Length * StorageType.ByteLength(Table.StorageType);
            var bin = Table.Rom.Binary;
            var bytes = address >= 0 && address + length <= bin.Length ? Convert.ToHexString(bin, address, length) : "?";
            var spaced = string.Join(" ", Enumerable.Range(0, bytes.Length / 2).Select(i => bytes.Substring(i * 2, 2)));
            var active = Table is TableSwitch sw && sw is not TableBitwiseSwitch && sw.ActiveState == null ? "   (no state matches)" : "";
            _rawBytesLabel.Text = $"0x{address:X}: {spaced}{active}";
        }
    }

    // ----- events -----

    private void OnTableDataChanged(object? sender, EventArgs e)
    {
        // Coalesce the many per-cell notifications of a bulk edit into one refresh.
        if (_refreshPending || IsDisposed || Disposing || !IsHandleCreated)
            return;
        _refreshPending = true;
        BeginInvoke(() =>
        {
            _refreshPending = false;
            if (IsDisposed)
                return;
            if (_comparedTable != null)
                Table.RefreshCompare();
            _grid?.RefreshValues();
            _scalar?.RefreshValue();
            _graph?.RefreshData();
            UpdatePresetButtons();
            _main.UpdateCellStatus(this);
        });
    }

    private void OnCompareTableChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed && !Disposing && IsHandleCreated)
            BeginInvoke(() =>
            {
                if (IsDisposed || Table.CompareTable == null)
                    return;
                Table.RefreshCompare();
                _grid?.RefreshValues();
                _graph?.RefreshData();
            });
    }

    private void OnScaleChanged(object? sender, EventArgs e)
    {
        UpdateLabels();
        _grid?.AutoSizeCells();
        _grid?.RefreshValues();
        _scalar?.RefreshValue();
        _graph?.RefreshData();
        _content.PerformLayout();
        if (_main.ActiveMdiChild == this)
            _main.Toolbar.Bind(this);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _main.OnTableActivated(this);
    }

    /// <summary>Bounds to open at (the table's remembered window), or null to fit the table.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Rectangle? InitialBounds { get; set; }

    private Rectangle? _boundsBeforeFit;
    private Rectangle _fittedBounds;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // MDI applies a default size and position when the child is shown, so size here.
        if (InitialBounds is { } bounds && _scalar != null)
        {
            // The scalar window has a fixed size: only its position is remembered.
            Location = bounds.Location;
            SizeToContent();
        }
        else if (InitialBounds is { } saved)
        {
            Bounds = saved;
            // Tables of one or two rows (1D/2D) are only ever as tall as their rows: never restore one
            // shorter than that, e.g. a remembered height that left no room for the scrollbar.
            if (_grid is { RowCount: <= 2 })
            {
                int needed = Math.Min(RequiredClientHeight(ClientSize.Width), Math.Max(150, _main.MdiClientArea.Height - 20));
                if (ClientSize.Height < needed)
                    ClientSize = new Size(ClientSize.Width, needed);
            }
            KeepInView();
        }
        else
        {
            SizeToContent();
        }
    }

    private const int WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int HTCAPTION = 2;

    protected override void WndProc(ref Message m)
    {
        // Double-clicking the title bar toggles between fitting the table and the previous size,
        // in place of maximize/restore.
        if (m.Msg == WM_NCLBUTTONDBLCLK && m.WParam == HTCAPTION)
        {
            ToggleFit();
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Fits the window to its table, or returns to the size and place it had before.</summary>
    public void ToggleFit()
    {
        if (WindowState != FormWindowState.Normal)
        {
            WindowState = FormWindowState.Normal;
            return;
        }
        if (_boundsBeforeFit is { } previous && Bounds == _fittedBounds)
        {
            Bounds = previous;
            _boundsBeforeFit = null;
            KeepInView();
            return;
        }
        FitToTable();
    }

    /// <summary>Sizes the window to show the whole table, remembering the size to return to.</summary>
    public void FitToTable()
    {
        var before = Bounds;
        SizeToContent();
        if (Bounds != before)
        {
            _boundsBeforeFit = before;
            _fittedBounds = Bounds;
        }
    }

    /// <summary>Moves (and if necessary shrinks) the window so it lies inside the editor area.</summary>
    public void KeepInView()
    {
        if (WindowState != FormWindowState.Normal)
            return;
        var area = _main.MdiClientArea;
        if (area.Width <= 0 || area.Height <= 0)
            return;
        int w = Math.Max(MinimumSize.Width, Math.Min(Width, area.Width));
        int h = Math.Max(MinimumSize.Height, Math.Min(Height, area.Height));
        int x = Math.Max(0, Math.Min(Left, area.Width - w));
        int y = Math.Max(0, Math.Min(Top, area.Height - h));
        var target = new Rectangle(x, y, w, h);
        if (target != Bounds)
            Bounds = target;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _scalar?.FocusValue();
        if (_grid != null && _grid.RowCount > 0)
        {
            // Start on the first data cell.
            var first = Table.DataCells.FirstOrDefault();
            var pos = first != null ? _grid.FindCell(first) : null;
            if (pos is { } p)
                _grid.CurrentCell = _grid.Rows[p.Y].Cells[p.X];
            _grid.Focus();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _tips.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Table.DataChanged -= OnTableDataChanged;
        Table.ScaleChanged -= OnScaleChanged;
        if (_comparedTable != null)
            _comparedTable.DataChanged -= OnCompareTableChanged;
        if (Table.CompareTable != null)
            Table.SetCompareTable(null);
        Table.ClearSelection();
        _main.OnTableClosed(this);
        base.OnFormClosed(e);
    }

    // ----- editing -----

    /// <summary>Runs an edit on this table as one undo step, reporting user level / lock problems.</summary>
    public void RunEdit(string name, Action<Table> action)
    {
        if (Table.Locked)
        {
            _main.SetStatus($"{Table.Name} is locked.");
            return;
        }
        try
        {
            using (Table.Rom?.History.Begin(name))
                action(Table);
        }
        catch (UserLevelException ex)
        {
            var hint = ex.Level > CoreSettings.Current.UserLevel ? "\nChange the user level in View > User Level." : "";
            MessageBox.Show(this, ex.Message + hint, "Table not modified",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (ImageReadOnlyException)
        {
            // A locked revision: nothing was written; offer a new revision to make the change in.
            _main.OnEditBlocked(Table.Rom);
        }
    }

    public void RunCommand(GridCommand command)
    {
        var toolbar = _main.Toolbar;
        switch (command)
        {
            case GridCommand.IncrementCoarse:
                RunEdit("Increment", t => t.Increment(toolbar.CoarseIncrement));
                break;
            case GridCommand.DecrementCoarse:
                RunEdit("Decrement", t => t.Increment(-toolbar.CoarseIncrement));
                break;
            case GridCommand.IncrementFine:
                RunEdit("Increment", t => t.Increment(toolbar.FineIncrement));
                break;
            case GridCommand.DecrementFine:
                RunEdit("Decrement", t => t.Increment(-toolbar.FineIncrement));
                break;
            case GridCommand.Multiply:
                Multiply(toolbar.ValueText);
                break;
            case GridCommand.Copy:
                CopySelection();
                break;
            case GridCommand.Paste:
                Paste();
                break;
            case GridCommand.Interpolate:
                RunEdit("Interpolate", t => t.Interpolate());
                break;
            case GridCommand.VerticalInterpolate:
                RunEdit("Vertical interpolate", t => t.VerticalInterpolate());
                break;
            case GridCommand.HorizontalInterpolate:
                RunEdit("Horizontal interpolate", t => t.HorizontalInterpolate());
                break;
            case GridCommand.Undo:
                _main.Undo(Rom);
                break;
            case GridCommand.Redo:
                _main.Redo(Rom);
                break;
        }
    }

    /// <summary>Whether the table has an axis stored in the image (curves and maps without label axes).</summary>
    public bool CanRescaleAxis => Table is Table2D or Table3D && Table.Axes.Any(a => !a.IsStaticDataTable);

    /// <summary>Rescales one axis and resamples the data along it (one undo step).</summary>
    public void RescaleAxis()
    {
        if (!CanRescaleAxis)
            return;
        // Start with the axis the cursor is on, if any.
        var initial = (_grid?.CurrentGridCell.Role, Table) switch
        {
            (CellRole.YAxis, Table3D t3) => t3.YAxis,
            (_, Table3D t3) => t3.XAxis,
            (_, Table2D t2) => t2.Axis,
            _ => null,
        };
        using var dialog = new RescaleAxisForm(Table, initial);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Plan is not { CanApply: true } plan)
            return;
        try
        {
            RunEdit("Rescale axis", _ => plan.Apply());
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Axis not rescaled", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var scale = Table.CurrentScale;
        _main.SetStatus(plan.MaxChange == 0
            ? $"Rescaled the axis of {Table.Name}; lookups within the old range are unchanged."
            : $"Rescaled the axis of {Table.Name}; largest change within the old range {ValueFormatter.Format(Math.Abs(plan.MaxChange), scale.Format)} {(scale.IsRaw ? "" : scale.Unit)}".TrimEnd() + ".");
    }

    public void SetValue(string text)
    {
        if (!ValueFormatter.TryParse(text, out _))
        {
            _main.SetStatus($"\"{text}\" is not a number.");
            return;
        }
        RunEdit("Set value", t => t.SetRealValue(text));
    }

    public void Multiply(string text)
    {
        if (!ValueFormatter.TryParse(text, out var factor))
        {
            _main.SetStatus("Enter a factor in the value box to multiply by.");
            return;
        }
        RunEdit($"Multiply by {text}", t => t.Multiply(factor));
    }

    public void CopySelection()
    {
        var text = TableClipboard.CopySelection(Table);
        if (text != null)
            SetClipboard(text);
    }

    public void CopyTable() => SetClipboard(TableClipboard.CopyTable(Table));

    private void SetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            _main.SetStatus("The clipboard is in use by another program.");
        }
    }

    public void Paste()
    {
        string text;
        try
        {
            text = Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return;
        }
        RunEdit("Paste", t => TableClipboard.Paste(t, text));
    }

    // ----- compare -----

    private void UpdateCompareMenu()
    {
        _compareOff.Checked = _comparedTable == null;
        _compareOriginal.Checked = ReferenceEquals(_comparedTable, Table);
        _displayAbsolute.Checked = _grid?.CompareDisplay == CompareDisplay.Absolute;
        _displayPercent.Checked = _grid?.CompareDisplay == CompareDisplay.Percent;
        _compareToCurrent.Checked = Table.CompareValueType == CompareValueType.Bin;
        _compareToOriginalValues.Checked = Table.CompareValueType == CompareValueType.Original;
        // Showing changes always compares against original values.
        bool otherRom = _comparedTable != null && !ReferenceEquals(_comparedTable, Table);
        _compareToCurrent.Enabled = _compareToOriginalValues.Enabled = otherRom;

        _compareRomMenu.DropDownItems.Clear();
        foreach (var rom in _main.OpenRoms.Where(r => !ReferenceEquals(r, Table.Rom)))
        {
            var other = rom.GetTable(Table.Name);
            var item = new ToolStripMenuItem(rom.ToString())
            {
                Enabled = other != null && Table.IsCompatibleWith(other),
                Checked = other != null && ReferenceEquals(_comparedTable, other),
            };
            if (other != null)
                item.Click += (_, _) => SetCompare(other);
            _compareRomMenu.DropDownItems.Add(item);
        }
        if (_compareRomMenu.DropDownItems.Count == 0)
            _compareRomMenu.DropDownItems.Add(new ToolStripMenuItem("(open another image to compare)") { Enabled = false });
    }

    public void SetCompare(Table? other)
    {
        if (_comparedTable != null)
            _comparedTable.DataChanged -= OnCompareTableChanged;
        _comparedTable = other;
        if (other == null)
        {
            Table.SetCompareTable(null);
        }
        else if (ReferenceEquals(other, Table))
        {
            Table.CompareToOriginal();
        }
        else
        {
            Table.SetCompareTable(other);
            other.DataChanged += OnCompareTableChanged;
        }
        UpdateLabels();
        _grid?.AutoSizeCells();
        _grid?.RefreshValues();
        _graph?.RefreshData();
        if (_main.ActiveMdiChild == this)
            _main.Toolbar.Bind(this);
    }

    private void CompareToOriginal() => SetCompare(Table);

    private void SetCompareDisplay(CompareDisplay display)
    {
        if (_grid == null)
            return;
        _grid.CompareDisplay = display;
        UpdateLabels();
        _grid.AutoSizeCells();
        _grid.RefreshValues();
    }

    private void SetCompareValueType(CompareValueType type)
    {
        Table.CompareValueType = type;
        foreach (var axis in Table.Axes)
            axis.CompareValueType = type;
        if (_comparedTable != null)
            Table.RefreshCompare();
        _grid?.RefreshValues();
    }

    /// <summary>Re-reads labels and sizes after a settings change.</summary>
    public void ApplySettings()
    {
        UpdateLabels();
        _grid?.AutoSizeCells();
        _grid?.RefreshValues();
        _scalar?.RefreshValue();
        UpdatePresetButtons();
    }
}

/// <summary>A label drawn rotated 90° counter-clockwise, for the Y axis caption.</summary>
public sealed class VerticalLabel : Control
{
    public VerticalLabel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(0, Height);
        e.Graphics.RotateTransform(-90);
        // TextRenderer ignores world transforms, so use GDI+ text here.
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using var brush = new SolidBrush(ForeColor);
        e.Graphics.DrawString(Text, Font, brush, new RectangleF(0, 0, Height, Width), format);
        e.Graphics.Restore(state);
    }
}
