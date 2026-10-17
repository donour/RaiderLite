using System.Collections.Concurrent;
using System.Globalization;
using RaiderLite.Core.Definitions;
using RaiderLite.Core.Model;

namespace RaiderLite;

/// <summary>The editor window: ROM/table tree on the left, table windows in the MDI area.</summary>
public sealed class MainForm : Form
{
    /// <summary>Image file types. Lotus .cpt files are raw region images, read like .bin.</summary>
    private const string ImageFileFilter =
        "ECU images (*.cpt;*.bin;*.hex;*.rom)|*.cpt;*.bin;*.hex;*.rom|Lotus images (*.cpt)|*.cpt|Binary images (*.bin)|*.bin|All files (*.*)|*.*";

    private readonly List<Rom> _roms = [];
    private readonly Dictionary<Table, TableForm> _openTables = [];
    private readonly Dictionary<Table, TreeNode> _tableNodes = [];
    private readonly HashSet<Table> _boldTables = [];
    private readonly ConcurrentDictionary<string, (DateTime Stamp, DefinitionDocument Doc)> _definitionCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly MenuStrip _menu = new();
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true, BorderStyle = BorderStyle.None };
    private readonly TextBox _filter = new() { Dock = DockStyle.Top, PlaceholderText = "Filter tables (Ctrl+F)" };
    private readonly Panel _treePanel = new() { Dock = DockStyle.Left, Width = 320 };
    private readonly Splitter _splitter = new() { Dock = DockStyle.Left, Width = 4 };
    private readonly Panel _status = new() { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(6, 0, 18, 0) };
    private readonly Label _statusText = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false };
    private readonly Label _cellInfo = new() { Dock = DockStyle.Right, AutoSize = true, TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false, Padding = new Padding(12, 5, 0, 0) };
    private readonly ToolStripMenuItem _recentMenu = new("Open &Recent");
    private readonly ToolStripMenuItem _undoItem = new("&Undo");
    private readonly ToolStripMenuItem _redoItem = new("&Redo");
    private readonly ToolStripMenuItem[] _userLevelItems = new ToolStripMenuItem[5];
    private readonly ToolStripMenuItem _displayHighTables = new("Display &High Level Tables");
    private readonly ToolStripMenuItem _sortTree = new("&Sort Tables Alphabetically");
    private readonly System.Windows.Forms.Timer _filterTimer = new() { Interval = 250 };
    private MdiClient? _mdiClient;
    private TableForm? _activeTable;

    public MainForm(string[] filesToOpen)
    {
        Text = "RaiderLite";
        IsMdiContainer = true;
        Font = UiFonts.Regular;
        AllowDrop = true;
        KeyPreview = true;
        Icon = SystemIcons.Application;

        Toolbar = new RibbonBar(this);
        BuildMenu();

        // Fill first in z-order so the right-docked cell info keeps its width.
        _status.Controls.Add(_statusText);
        _status.Controls.Add(_cellInfo);
        _tree.ImageList = TreeIcons.Create(DeviceDpi);
        DpiChanged += (_, _) =>
        {
            var old = _tree.ImageList;
            _tree.ImageList = TreeIcons.Create(DeviceDpi);
            old?.Dispose();
        };
        _treePanel.Controls.Add(_tree);
        _treePanel.Controls.Add(_filter);

        // Dock order: added last docks first.
        Controls.Add(_splitter);
        Controls.Add(_treePanel);
        Controls.Add(_status);
        Controls.Add(Toolbar);
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        _mdiClient = Controls.OfType<MdiClient>().FirstOrDefault();
        if (_mdiClient != null)
            _mdiClient.BackColor = SystemColors.AppWorkspace;

        _tree.NodeMouseDoubleClick += (_, e) => { if (!AppSettings.Current.SingleClickTables) OpenNode(e.Node); };
        _tree.NodeMouseClick += TreeNodeMouseClick;
        _tree.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _tree.SelectedNode != null)
            {
                OpenNode(_tree.SelectedNode);
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        _tree.AfterSelect += (_, _) => UpdateTreeSelectionStatus();
        _filter.TextChanged += (_, _) =>
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        };
        _filter.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                _filter.Clear();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                _tree.Focus();
                e.SuppressKeyPress = true;
            }
        };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            RebuildTree();
        };
        MdiChildActivate += (_, _) =>
        {
            if (ActiveMdiChild == null)
                OnTableActivated(null);
        };

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var f in files)
                    await OpenImageAsync(f);
            }
        };

        RestoreWindow();
        UpdateUserLevelMenu();
        UpdateRecentMenu();
        UpdateUndoMenu();
        SetStatus(AppSettings.Current.DefinitionFiles.Count == 0
            ? "No definition files configured: use Edit > Definition Manager to add some."
            : "Ready.");

        Shown += async (_, _) =>
        {
            foreach (var f in filesToOpen)
                await OpenImageAsync(f);
        };
    }

    /// <summary>The ribbon; its table commands act on the active table window.</summary>
    public RibbonBar Toolbar { get; }

    public IReadOnlyList<Rom> OpenRoms => _roms;

    /// <summary>Size of the MDI area that table windows are fitted into.</summary>
    public Size MdiClientArea => _mdiClient?.ClientSize ?? ClientSize;

    // ----- menus -----

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("&Open Image...", null, async (_, _) => await OpenImageDialogAsync(), Keys.Control | Keys.O));
        file.DropDownItems.Add(_recentMenu);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("&Save Image", null, (_, _) => SaveRom(SelectedRom, saveAs: false), Keys.Control | Keys.S));
        file.DropDownItems.Add(new ToolStripMenuItem("Save Image &As...", null, (_, _) => SaveRom(SelectedRom, saveAs: true), Keys.Control | Keys.Shift | Keys.S));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("&Close Image", null, (_, _) => CloseRom(SelectedRom)));
        file.DropDownItems.Add(new ToolStripMenuItem("Close All &Images", null, (_, _) => CloseAllRoms()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Image &Properties...", null, (_, _) => ShowRomProperties(SelectedRom)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));

        var edit = new ToolStripMenuItem("&Edit");
        _undoItem.ShortcutKeyDisplayString = "Ctrl+Z";
        _redoItem.ShortcutKeyDisplayString = "Ctrl+Y";
        _undoItem.Click += (_, _) => Undo(SelectedRom);
        _redoItem.Click += (_, _) => Redo(SelectedRom);
        edit.DropDownItems.Add(_undoItem);
        edit.DropDownItems.Add(_redoItem);
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(new ToolStripMenuItem("&Definition Manager...", null, (_, _) => ShowDefinitionManager()));
        edit.DropDownItems.Add(new ToolStripMenuItem("&Settings...", null, (_, _) => ShowSettings()));
        edit.DropDownOpening += (_, _) => UpdateUndoMenu();

        var view = new ToolStripMenuItem("&View");
        var userLevel = new ToolStripMenuItem("&User Level");
        for (int i = 0; i < 5; i++)
        {
            int level = i + 1;
            _userLevelItems[i] = new ToolStripMenuItem($"{level} - {AppSettings.UserLevelNames[i]}", null, (_, _) => SetUserLevel(level));
            userLevel.DropDownItems.Add(_userLevelItems[i]);
        }
        view.DropDownItems.Add(userLevel);
        _displayHighTables.Click += (_, _) =>
        {
            AppSettings.Current.DisplayHighTables = !AppSettings.Current.DisplayHighTables;
            UpdateUserLevelMenu();
            RebuildTree();
        };
        _sortTree.Click += (_, _) =>
        {
            AppSettings.Current.SortTableTree = !AppSettings.Current.SortTableTree;
            UpdateUserLevelMenu();
            RebuildTree();
        };
        view.DropDownItems.Add(_displayHighTables);
        view.DropDownItems.Add(_sortTree);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(new ToolStripMenuItem("&Expand All", null, (_, _) => _tree.ExpandAll()));
        view.DropDownItems.Add(new ToolStripMenuItem("&Collapse All", null, (_, _) => CollapseToRoms()));
        view.DropDownItems.Add(new ToolStripMenuItem("&Find Table", null, (_, _) => _filter.Focus(), Keys.Control | Keys.F));

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add(new ToolStripMenuItem("&Compare Images...", null, (_, _) => ShowCompareImages()));

        var window = new ToolStripMenuItem("&Window");
        window.DropDownItems.Add(new ToolStripMenuItem("&Cascade", null, (_, _) => LayoutMdi(MdiLayout.Cascade)));
        window.DropDownItems.Add(new ToolStripMenuItem("Tile &Horizontally", null, (_, _) => LayoutMdi(MdiLayout.TileHorizontal)));
        window.DropDownItems.Add(new ToolStripMenuItem("Tile &Vertically", null, (_, _) => LayoutMdi(MdiLayout.TileVertical)));
        window.DropDownItems.Add(new ToolStripMenuItem("Close &All Tables", null, (_, _) => CloseAllTables()));
        window.DropDownItems.Add(new ToolStripSeparator());
        _menu.MdiWindowListItem = window;

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(new ToolStripMenuItem("&Keyboard Shortcuts", null, (_, _) => ShowShortcuts()));
        help.DropDownItems.Add(new ToolStripMenuItem("&About RaiderLite", null, (_, _) => MessageBox.Show(this,
            "RaiderLite\n\nAn ECU map editor using RomRaider XML definitions.\nA C# port of RomRaider's table editor (editing only).",
            "About RaiderLite", MessageBoxButtons.OK, MessageBoxIcon.Information)));

        _menu.Items.AddRange([file, edit, view, tools, window, help]);
    }

    private void UpdateUserLevelMenu()
    {
        for (int i = 0; i < 5; i++)
            _userLevelItems[i].Checked = AppSettings.Current.UserLevel == i + 1;
        _displayHighTables.Checked = AppSettings.Current.DisplayHighTables;
        _sortTree.Checked = AppSettings.Current.SortTableTree;
    }

    private void SetUserLevel(int level)
    {
        AppSettings.Current.UserLevel = level;
        AppSettings.Current.ApplyToCore();
        UpdateUserLevelMenu();
        RebuildTree();
        foreach (var form in _openTables.Values)
            form.ApplySettings();
        SetStatus($"User level: {level} - {AppSettings.UserLevelNames[level - 1]}");
    }

    private void UpdateRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();
        foreach (var path in AppSettings.Current.RecentFiles)
        {
            var p = path;
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem(p, null, async (_, _) => await OpenImageAsync(p)));
        }
        _recentMenu.Enabled = _recentMenu.DropDownItems.Count > 0;
    }

    private void UpdateUndoMenu()
    {
        var history = SelectedRom?.History;
        _undoItem.Enabled = history?.CanUndo == true;
        _redoItem.Enabled = history?.CanRedo == true;
        _undoItem.Text = history?.UndoName is { } u ? $"&Undo {u}" : "&Undo";
        _redoItem.Text = history?.RedoName is { } r ? $"&Redo {r}" : "&Redo";
        Toolbar.UpdateState(SelectedRom);
    }

    private void ShowShortcuts()
    {
        const string text = """
            In a table:
              Type a number          start entering a value; Enter sets the selected cells
              +  /  _                increment / decrement by the coarse step
              Ctrl+Up / Ctrl+Down    increment / decrement by the coarse step
              ]  /  [                increment / decrement by the fine step
              Ctrl+Shift+Up / Down   increment / decrement by the fine step
              *  or  Ctrl+Enter      multiply selected cells by the value box
              Shift+I / V / H        interpolate / vertically / horizontally
              Ctrl+C / Ctrl+V        copy selection / paste (RomRaider and spreadsheet formats)
              Ctrl+Z / Ctrl+Y        undo / redo
              Ctrl+A                 select all cells (click the top-left corner for data only)

            Main window:
              Ctrl+O  open image     Ctrl+S  save     Ctrl+Shift+S  save as
              Ctrl+F  filter tables  Enter in the tree opens the selected table
              Click a table in the tree to open it, click it again to close it
              Esc closes the active table window
            """;
        MessageBox.Show(this, text, "Keyboard Shortcuts", MessageBoxButtons.OK, MessageBoxIcon.None);
    }

    // ----- window state -----

    private void RestoreWindow()
    {
        var s = AppSettings.Current;
        _treePanel.Width = Math.Clamp(s.TreeWidth, 150, 900);
        if (s.WindowBounds.Width > 200 && s.WindowBounds.Height > 200 &&
            Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(s.WindowBounds)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = s.WindowBounds;
        }
        else
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            Size = new Size(Math.Min(1400, area.Width - 40), Math.Min(900, area.Height - 40));
            StartPosition = FormStartPosition.CenterScreen;
        }
        if (s.WindowMaximized)
            WindowState = FormWindowState.Maximized;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        foreach (var rom in _roms.ToList())
        {
            if (!ConfirmDiscard(rom))
            {
                e.Cancel = true;
                return;
            }
        }
        var s = AppSettings.Current;
        s.WindowMaximized = WindowState == FormWindowState.Maximized;
        s.WindowBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        s.TreeWidth = _treePanel.Width;
        s.Save();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Table windows report their bounds as they close, which can follow FormClosing.
        AppSettings.Current.Save();
        base.OnFormClosed(e);
    }

    // ----- status -----

    public void SetStatus(string text) => _statusText.Text = text;

    /// <summary>The image the image commands act on: the active table's, else the tree selection's.</summary>
    internal Rom? CurrentRom => SelectedRom;

    internal void FocusFilter() => _filter.Focus();

    private Rom? SelectedRom
    {
        get
        {
            if (_activeTable?.Rom is { } r && ActiveMdiChild == _activeTable && (_tree.Focused == false))
                return r;
            for (var node = _tree.SelectedNode; node != null; node = node.Parent)
            {
                if (node.Tag is Rom rom)
                    return rom;
                if (node.Tag is Table t)
                    return t.Rom;
            }
            return _activeTable?.Rom ?? (_roms.Count == 1 ? _roms[0] : null);
        }
    }

    private void UpdateTreeSelectionStatus()
    {
        UpdateUndoMenu();
        if (_tree.SelectedNode?.Tag is Table t)
        {
            var scale = t.CurrentScale;
            SetStatus($"{t.Name}  |  {t.ShapeDescription}  |  0x{t.StorageAddress:X}  |  {StorageType.Describe(t.StorageType, t.Signed)}" +
                      (scale != null && !scale.IsRaw ? $"  |  {scale.Unit}" : ""));
        }
    }

    /// <summary>Shows the current cell and selection of a table window in the status bar.</summary>
    public void UpdateCellStatus(TableForm form)
    {
        if (form != _activeTable)
            return;
        // A single-value window has no grid: report its one cell.
        var gc = form.Grid?.CurrentGridCell
                 ?? (form.Table.Shape == TableShape.Scalar ? new GridCell(CellRole.Data, form.Table.DataCells.First()) : default);
        var parts = new List<string>();
        if (gc.Cell is { IsStatic: false } cell)
        {
            var scale = cell.Table.CurrentScale;
            var current = form.Grid?.CurrentCell;
            if (form.Table is Table3D t3 && gc.Role == CellRole.Data && current != null)
            {
                parts.Add($"X: {AxisText(t3.XAxis, current.ColumnIndex - 1)}");
                parts.Add($"Y: {AxisText(t3.YAxis, current.RowIndex - 1)}");
            }
            else if (form.Table is Table2D t2 && gc.Role == CellRole.Data && current != null)
            {
                parts.Add($"X: {AxisText(t2.Axis, current.ColumnIndex)}");
            }
            parts.Add($"{ValueFormatter.Format(cell.RealValue, scale?.Format)} {(scale is { IsRaw: false } ? scale.Unit : "")}".Trim());
            parts.Add($"raw {cell.BinValue.ToString(CultureInfo.CurrentCulture)} (0x{(long)cell.BinValue & 0xFFFFFFFF:X})");
            parts.Add($"@0x{cell.Address:X}");
        }

        var selected = form.Table.SelectedCells.Where(c => !c.IsStatic).ToList();
        if (selected.Count > 1)
        {
            var values = selected.Select(c => c.RealValue).ToList();
            var fmt = form.Table.CurrentScale?.Format;
            parts.Add($"{selected.Count} selected: min {ValueFormatter.Format(values.Min(), fmt)}, max {ValueFormatter.Format(values.Max(), fmt)}, avg {ValueFormatter.Format(values.Average(), "0.###")}");
        }
        SetCellInfo(string.Join("   ", parts));
    }

    private void SetCellInfo(string text)
    {
        _cellInfo.Text = text;
    }

    private static string AxisText(Table1D axis, int index)
    {
        if (index < 0 || index >= axis.Cells.Length)
            return "";
        var cell = axis.Cells[index];
        if (cell.IsStatic)
            return cell.StaticText ?? "";
        var scale = axis.CurrentScale;
        return $"{ValueFormatter.Format(cell.RealValue, scale?.Format)} {(scale is { IsRaw: false } ? scale.Unit : "")}".Trim();
    }

    // ----- opening images -----

    internal async Task OpenImageDialogAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open Image",
            Filter = ImageFileFilter,
            Multiselect = true,
            InitialDirectory = Directory.Exists(AppSettings.Current.LastImageDir) ? AppSettings.Current.LastImageDir : "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        AppSettings.Current.LastImageDir = Path.GetDirectoryName(dialog.FileNames[0]) ?? "";
        foreach (var f in dialog.FileNames)
            await OpenImageAsync(f);
    }

    private DefinitionDocument LoadDefinition(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);
        if (_definitionCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            return cached.Doc;
        var doc = DefinitionDocument.Load(path);
        _definitionCache[path] = (stamp, doc);
        return doc;
    }

    public async Task OpenImageAsync(string path)
    {
        if (_roms.Any(r => string.Equals(r.FilePath, path, StringComparison.OrdinalIgnoreCase)))
        {
            if (MessageBox.Show(this, $"{Path.GetFileName(path)} is already open. Open another copy?", "Open Image",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
        }

        byte[] image;
        try
        {
            // ECU images are kilobytes to a few megabytes; refuse anything absurd rather than run out of memory.
            const long maxImageSize = 64L * 1024 * 1024;
            if (new FileInfo(path).Length > maxImageSize)
            {
                MessageBox.Show(this, $"{Path.GetFileName(path)} is larger than 64 MB and doesn't look like an ECU image.", "Open Image",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            image = await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Open Image", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        UseWaitCursor = true;
        SetStatus($"Finding a definition for {Path.GetFileName(path)}...");
        try
        {
            var definitionFiles = AppSettings.Current.DefinitionFiles.ToList();
            var errors = new List<string>();
            var match = await Task.Run(() =>
            {
                foreach (var defPath in definitionFiles)
                {
                    if (!File.Exists(defPath))
                    {
                        errors.Add($"Definition file not found: {defPath}");
                        continue;
                    }
                    try
                    {
                        var doc = LoadDefinition(defPath);
                        if (doc.FindMatch(image) is { } info)
                            return (doc, info);
                    }
                    catch (DefinitionException ex)
                    {
                        errors.Add(ex.Message);
                    }
                }
                return ((DefinitionDocument, RomDefinitionInfo)?)null;
            });
            UseWaitCursor = false;
            foreach (var error in errors)
                SetStatus(error);

            (DefinitionDocument Doc, RomDefinitionInfo Info)? chosen = match;
            chosen ??= ChooseDefinitionManually(path, image, errors);
            if (chosen is not { } c)
            {
                SetStatus("Open cancelled.");
                return;
            }

            UseWaitCursor = true;
            SetStatus($"Loading {Path.GetFileName(path)} with {c.Info.Id.XmlId}...");
            var rom = await Task.Run(() => RomUnmarshaller.Load(c.Doc, c.Info.Node, image, path));
            AddRom(rom);
            AppSettings.Current.AddRecentFile(path);
            UpdateRecentMenu();
            ReportLoad(rom);
        }
        catch (DefinitionException ex)
        {
            MessageBox.Show(this, ex.Message, $"Error loading {Path.GetFileName(path)}", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Error loading image.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Anything else from a malformed definition or image: report it, don't take the editor down.
            ErrorReporter.Log(ex);
            MessageBox.Show(this, $"{Path.GetFileName(path)} could not be loaded:\n\n{ex.GetType().Name}: {ex.Message}",
                "Error Loading Image", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Error loading image.");
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// <summary>No configured definition matched: let the user pick a file and, if needed, a rom in it.</summary>
    private (DefinitionDocument, RomDefinitionInfo)? ChooseDefinitionManually(string imagePath, byte[] image, List<string> errors)
    {
        var message = $"No definition matched {Path.GetFileName(imagePath)}.";
        if (AppSettings.Current.DefinitionFiles.Count == 0)
            message = "No definition files are configured.";
        if (errors.Count > 0)
            message += "\n\n" + string.Join("\n", errors.Take(5));
        if (MessageBox.Show(this, message + "\n\nChoose a definition file to open it with?", "Definition Not Found",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return null;

        using var dialog = new OpenFileDialog
        {
            Title = "Choose ECU Definition",
            Filter = "RomRaider definitions (*.xml)|*.xml|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(AppSettings.Current.LastDefinitionDir) ? AppSettings.Current.LastDefinitionDir : "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return null;
        AppSettings.Current.LastDefinitionDir = Path.GetDirectoryName(dialog.FileName) ?? "";

        DefinitionDocument doc;
        try
        {
            doc = LoadDefinition(dialog.FileName);
        }
        catch (DefinitionException ex)
        {
            MessageBox.Show(this, ex.Message, "Definition Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }

        var info = doc.FindMatch(image);
        if (info == null)
        {
            using var picker = new RomPickerForm(doc, Path.GetFileName(imagePath));
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected == null)
                return null;
            info = picker.Selected;
        }

        if (!AppSettings.Current.DefinitionFiles.Contains(dialog.FileName, StringComparer.OrdinalIgnoreCase) &&
            MessageBox.Show(this, $"Add {Path.GetFileName(dialog.FileName)} to the definition list?", "Definition Manager",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            AppSettings.Current.DefinitionFiles.Add(dialog.FileName);
            AppSettings.Current.Save();
        }
        return (doc, info);
    }

    private void ReportLoad(Rom rom)
    {
        var status = $"Loaded {rom.FileName}: {rom.RomId}, {rom.Tables.Count} tables";
        if (rom.TotalChecksums > 0)
        {
            int valid = rom.ValidateChecksums();
            status += $", checksums {valid}/{rom.TotalChecksums} valid";
            if (rom.ChecksumErrors.Count > 0)
            {
                MessageBox.Show(this, $"Some checksums in the definition can't be checked against {rom.FileName}:\n\n" +
                    string.Join("\n", rom.ChecksumErrors) + "\n\nThey will not be updated when saving. Check that the definition matches this image.",
                    "Checksum Problem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (valid != rom.TotalChecksums)
            {
                MessageBox.Show(this, $"{valid} of {rom.TotalChecksums} checksums are valid in {rom.FileName}.\nThey will be recalculated when the image is saved.",
                    "Checksum Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        if (rom.Warnings.Count > 0)
            status += $", {rom.Warnings.Count} warning(s) (see Image Properties)";
        SetStatus(status + ".");

        if (rom.FaultyTables.Count > 0)
        {
            MessageBox.Show(this,
                $"{rom.FaultyTables.Count} table(s) could not be read from {rom.FileName} and were removed:\n\n" +
                string.Join("\n", rom.FaultyTables.Take(20)) + (rom.FaultyTables.Count > 20 ? "\n..." : "") +
                "\n\nCheck that the definition matches this image.",
                "Definition Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AddRom(Rom rom)
    {
        _roms.Add(rom);
        rom.DirtyChanged += (_, _) => Post(() => UpdateRomNode(rom));
        rom.History.Changed += (_, _) => Post(UpdateUndoMenu);
        foreach (var table in rom.Tables)
            table.DataChanged += OnAnyTableChanged;
        RebuildTree();
        if (_tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Tag == rom) is { } node)
        {
            node.Expand();
            _tree.SelectedNode = node;
        }
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread later, unless the window is gone.</summary>
    private void Post(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
            return;
        BeginInvoke(() =>
        {
            if (!IsDisposed)
                action();
        });
    }

    // ----- saving / closing -----

    public bool SaveRom(Rom? rom, bool saveAs)
    {
        if (rom == null)
            return false;
        var path = rom.FilePath;
        if (saveAs || string.IsNullOrEmpty(path))
        {
            using var dialog = new SaveFileDialog
            {
                Title = $"Save {rom.FileName} As",
                Filter = ImageFileFilter,
                FileName = rom.FileName,
                InitialDirectory = Path.GetDirectoryName(rom.FilePath) ?? AppSettings.Current.LastImageDir,
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return false;
            path = dialog.FileName;
        }

        var temp = path + ".tmp";
        try
        {
            var bytes = rom.PrepareForSave(out int checksums);
            if (rom.ChecksumErrors.Count > 0 &&
                MessageBox.Show(this, "These checksums could not be updated, so the saved image may be rejected by the ECU:\n\n" +
                        string.Join("\n", rom.ChecksumErrors) + "\n\nSave anyway?",
                    "Checksum Problem", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return false;
            }
            // Write to a temporary file first so a failed write can't truncate the image.
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            rom.FilePath = path;
            rom.IsDirty = false;
            AppSettings.Current.AddRecentFile(path);
            UpdateRecentMenu();
            UpdateRomNode(rom);
            foreach (var form in _openTables.Values.Where(f => f.Rom == rom))
                form.Text = $"{form.Table.Name} - {rom.FileName}";
            SetStatus($"Saved {path}" + (rom.TotalChecksums > 0 ? $" ({checksums} checksum(s) updated)." : "."));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Leave it; the original image is untouched either way.
            }
            MessageBox.Show(this, ex.Message, "Save Image", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancelled.</summary>
    private bool ConfirmDiscard(Rom rom)
    {
        if (!rom.IsDirty)
            return true;
        var answer = MessageBox.Show(this, $"{rom.FileName} has unsaved changes. Save them?", "Unsaved Changes",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        return answer switch
        {
            DialogResult.Yes => SaveRom(rom, saveAs: false),
            DialogResult.No => true,
            _ => false,
        };
    }

    public void CloseRom(Rom? rom)
    {
        if (rom == null || !ConfirmDiscard(rom))
            return;
        foreach (var form in _openTables.Values.Where(f => f.Rom == rom).ToList())
            form.Close();
        // Other images' tables may be comparing against this one.
        foreach (var form in _openTables.Values.Where(f => f.Table.CompareTable?.Rom == rom && f.Rom != rom).ToList())
            form.SetCompare(null);
        foreach (var table in rom.Tables)
            table.DataChanged -= OnAnyTableChanged;
        _roms.Remove(rom);
        RebuildTree();
        UpdateUndoMenu();
        SetStatus($"Closed {rom.FileName}.");
    }

    private void CloseAllRoms()
    {
        foreach (var rom in _roms.ToList())
        {
            CloseRom(rom);
            if (_roms.Contains(rom))
                return;
        }
    }

    private void CloseAllTables()
    {
        foreach (var form in _openTables.Values.ToList())
            form.Close();
    }

    // ----- undo -----

    public void Undo(Rom? rom)
    {
        if (rom?.History.CanUndo != true)
            return;
        var name = rom.History.UndoName;
        rom.History.Undo();
        SetStatus($"Undid {name}.");
        UpdateUndoMenu();
    }

    public void Redo(Rom? rom)
    {
        if (rom?.History.CanRedo != true)
            return;
        var name = rom.History.RedoName;
        rom.History.Redo();
        SetStatus($"Redid {name}.");
        UpdateUndoMenu();
    }

    // ----- tree -----

    private void RebuildTree()
    {
        var settings = AppSettings.Current;
        var filter = _filter.Text.Trim();
        bool filtering = filter.Length > 0;
        var selectedTag = _tree.SelectedNode?.Tag;
        var expanded = new HashSet<string>(_tree.Nodes.Cast<TreeNode>().SelectMany(AllNodes).Where(n => n.IsExpanded).Select(NodeKey));

        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            _tableNodes.Clear();
            _boldTables.Clear();
            foreach (var rom in _roms)
            {
                var romNode = new TreeNode(RomNodeText(rom)) { Tag = rom, ToolTipText = rom.FilePath, ImageIndex = TreeIcons.Rom, SelectedImageIndex = TreeIcons.Rom };
                romNode.NodeFont = UiFonts.Bold;
                var tables = rom.Tables.AsEnumerable();
                if (settings.SortTableTree)
                    tables = tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase);

                int shown = 0;
                foreach (var table in tables)
                {
                    bool tooHigh = table.UserLevel > settings.UserLevel;
                    if (tooHigh && !settings.DisplayHighTables)
                        continue;
                    if (filtering && !table.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parent = romNode;
                    foreach (var category in table.Category.Split("//"))
                    {
                        var existing = parent.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Tag is string && string.Equals(n.Text, category, StringComparison.OrdinalIgnoreCase));
                        if (existing == null)
                        {
                            existing = new TreeNode(category) { Tag = category, ImageIndex = TreeIcons.Category, SelectedImageIndex = TreeIcons.Category };
                            InsertCategory(parent, existing, settings.SortTableTree);
                        }
                        parent = existing;
                    }

                    var node = new TreeNode(table.Name) { Tag = table, ToolTipText = TableToolTip(table) };
                    node.ImageIndex = node.SelectedImageIndex = TreeIcons.For(table);
                    if (tooHigh)
                        node.ForeColor = SystemColors.GrayText;
                    if (table.IsChanged)
                    {
                        node.NodeFont = UiFonts.Bold;
                        _boldTables.Add(table);
                    }
                    parent.Nodes.Add(node);
                    _tableNodes[table] = node;
                    shown++;
                }

                if (filtering && shown == 0)
                    romNode.Nodes.Add(new TreeNode("(no matching tables)") { ForeColor = SystemColors.GrayText, ImageIndex = TreeIcons.None, SelectedImageIndex = TreeIcons.None });
                _tree.Nodes.Add(romNode);
            }

            foreach (var node in _tree.Nodes.Cast<TreeNode>().SelectMany(AllNodes))
            {
                if (filtering || expanded.Contains(NodeKey(node)))
                    node.Expand();
                if (selectedTag != null && ReferenceEquals(node.Tag, selectedTag))
                    _tree.SelectedNode = node;
            }
            if (!filtering)
            {
                foreach (TreeNode romNode in _tree.Nodes)
                    romNode.Expand();
            }
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private static void InsertCategory(TreeNode parent, TreeNode category, bool sorted)
    {
        // Categories go above tables, in definition order unless sorting.
        int index = 0;
        foreach (TreeNode n in parent.Nodes)
        {
            if (n.Tag is not string)
                break;
            if (sorted && string.Compare(n.Text, category.Text, StringComparison.OrdinalIgnoreCase) > 0)
                break;
            index++;
        }
        parent.Nodes.Insert(index, category);
    }

    private static IEnumerable<TreeNode> AllNodes(TreeNode node)
    {
        yield return node;
        foreach (TreeNode child in node.Nodes)
        {
            foreach (var n in AllNodes(child))
                yield return n;
        }
    }

    private static string NodeKey(TreeNode node) => node.FullPath + "|" + (node.Tag is Rom r ? r.GetHashCode().ToString(CultureInfo.InvariantCulture) : "");

    private static string RomNodeText(Rom rom) => (rom.IsDirty ? "* " : "") + $"{rom.FileName}  [{rom.RomId.XmlId}]";

    private static string TableToolTip(Table table)
    {
        var desc = table.Description.Trim();
        if (desc.Length > 400)
            desc = desc[..400] + "...";
        var header = $"{table.ShapeDescription} at 0x{table.StorageAddress:X}";
        return desc.Length > 0 ? header + "\n" + desc : header;
    }

    private void UpdateRomNode(Rom rom)
    {
        if (_tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Tag == rom) is { } node)
            node.Text = RomNodeText(rom);
        Text = _roms.Any(r => r.IsDirty) ? "RaiderLite *" : "RaiderLite";
    }

    private void OnAnyTableChanged(object? sender, EventArgs e)
    {
        if (sender is not Table table || !_tableNodes.TryGetValue(table.TopLevel, out var node))
            return;
        bool changed = table.TopLevel.IsChanged;
        if (changed == _boldTables.Contains(table.TopLevel))
            return;
        if (changed)
        {
            _boldTables.Add(table.TopLevel);
            node.NodeFont = UiFonts.Bold;
        }
        else
        {
            _boldTables.Remove(table.TopLevel);
            node.NodeFont = null;
        }
        node.Text = node.Text; // Re-measure for the new font.
    }

    private void CollapseToRoms()
    {
        _tree.CollapseAll();
        foreach (TreeNode n in _tree.Nodes)
            n.Expand();
    }

    private void TreeNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node == null)
            return;
        if (e.Button == MouseButtons.Right)
        {
            _tree.SelectedNode = e.Node;
            ShowTreeContextMenu(e.Node, e.Location);
        }
        else if (e.Button == MouseButtons.Left && AppSettings.Current.SingleClickTables && e.Node.Tag is Table table)
        {
            // Only clicks on the name or icon, not the expand glyph or blank space.
            var hit = _tree.HitTest(e.Location).Location;
            if (hit is TreeViewHitTestLocations.Label or TreeViewHitTestLocations.Image)
                ToggleTable(table);
        }
    }

    /// <summary>
    /// Single-click behaviour: opens a closed table, brings a hidden one to the front,
    /// and closes the table if it is already the active window.
    /// </summary>
    private void ToggleTable(Table table)
    {
        if (!_openTables.TryGetValue(table, out var form))
        {
            OpenTable(table);
            return;
        }
        bool justOpened = (DateTime.UtcNow - form.OpenedAt).TotalMilliseconds < SystemInformation.DoubleClickTime;
        if (ActiveMdiChild == form && form.WindowState != FormWindowState.Minimized)
        {
            // The second click of a habitual double-click shouldn't close what the first opened.
            if (!justOpened)
                form.Close();
        }
        else
        {
            OpenTable(table);
        }
    }

    private void ShowTreeContextMenu(TreeNode node, Point location)
    {
        var menu = new ContextMenuStrip();
        switch (node.Tag)
        {
            case Rom rom:
                menu.Items.Add("&Save", null, (_, _) => SaveRom(rom, false));
                menu.Items.Add("Save &As...", null, (_, _) => SaveRom(rom, true));
                menu.Items.Add("&Close", null, (_, _) => CloseRom(rom));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("&Properties...", null, (_, _) => ShowRomProperties(rom));
                break;
            case Table table:
                menu.Items.Add("&Open", null, (_, _) => OpenTable(table));
                menu.Items.Add("&Properties...", null, (_, _) => ShowTableProperties(table));
                break;
            default:
                menu.Dispose();
                return;
        }
        ShowAndDispose(menu, _tree, location);
    }

    /// <summary>Shows a one-off menu and disposes it once it has closed and its click has run.</summary>
    internal static void ShowAndDispose(ContextMenuStrip menu, Control anchor, Point location)
    {
        menu.Closed += (_, _) => anchor.BeginInvoke(menu.Dispose);
        menu.Show(anchor, location);
    }

    private void OpenNode(TreeNode? node)
    {
        if (node?.Tag is Table table)
            OpenTable(table);
    }

    // ----- table windows -----

    public TableForm OpenTable(Table table)
    {
        if (_openTables.TryGetValue(table, out var existing))
        {
            if (existing.WindowState == FormWindowState.Minimized)
                existing.WindowState = FormWindowState.Normal;
            existing.Activate();
            return existing;
        }
        var form = new TableForm(this, table)
        {
            MdiParent = this,
            InitialBounds = AppSettings.Current.GetTableWindow(TableWindowKey(table)),
        };
        _openTables[table] = form;
        form.Show();
        return form;
    }

    private static string TableWindowKey(Table table) => $"{table.Rom?.RomId.XmlId}|{table.Name}";

    public void OnTableActivated(TableForm? form)
    {
        _activeTable = form;
        Toolbar.Bind(form);
        SetCellInfo("");
        if (form != null)
        {
            UpdateCellStatus(form);
            if (_tableNodes.TryGetValue(form.Table, out var node) && !_tree.Focused)
                _tree.SelectedNode = node;
        }
        UpdateUndoMenu();
    }

    public void OnTableClosed(TableForm form)
    {
        // Remember where the table was, to reopen it there.
        if (form.WindowState == FormWindowState.Normal)
            AppSettings.Current.SetTableWindow(TableWindowKey(form.Table), form.Bounds);
        else
            AppSettings.Current.SetTableWindow(TableWindowKey(form.Table), form.RestoreBounds);
        _openTables.Remove(form.Table);
        if (_activeTable == form)
            OnTableActivated(null);
    }

    // ----- dialogs -----

    public void ShowTableProperties(Table table)
    {
        using var dialog = new PropertiesForm($"Table Properties - {table.Name}", PropertiesForm.DescribeTable(table), table.Description);
        dialog.ShowDialog(this);
    }

    internal void ShowRomProperties(Rom? rom)
    {
        if (rom == null)
            return;
        var notes = rom.Warnings.Count == 0 ? "No load warnings." : "Load warnings:" + Environment.NewLine + string.Join(Environment.NewLine, rom.Warnings);
        using var dialog = new PropertiesForm($"Image Properties - {rom.FileName}", PropertiesForm.DescribeRom(rom), notes);
        dialog.ShowDialog(this);
    }

    internal void ShowDefinitionManager()
    {
        using var dialog = new DefinitionManagerForm(AppSettings.Current.DefinitionFiles);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AppSettings.Current.DefinitionFiles = dialog.Files;
            AppSettings.Current.Save();
            SetStatus($"{dialog.Files.Count} definition file(s) configured.");
        }
    }

    internal void ShowSettings()
    {
        using var dialog = new SettingsForm(AppSettings.Current);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        AppSettings.Current.ApplyToCore();
        AppSettings.Current.Save();
        UpdateUserLevelMenu();
        RebuildTree();
        foreach (var form in _openTables.Values)
            form.ApplySettings();
    }

    internal void ShowCompareImages()
    {
        if (_roms.Count < 2)
        {
            MessageBox.Show(this, "Open two images to compare them.", "Compare Images", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var dialog = new CompareImagesForm(this, _roms);
        dialog.Show(this);
    }

    /// <summary>Opens a table in two images side by side, comparing the first against the second.</summary>
    public void OpenComparison(Table left, Table right)
    {
        var a = OpenTable(left);
        var b = OpenTable(right);
        a.SetCompare(right);
        if (_mdiClient != null)
        {
            var area = _mdiClient.ClientSize;
            a.Location = new Point(0, 0);
            b.Location = new Point(Math.Min(a.Width + 4, Math.Max(0, area.Width - b.Width)), 0);
        }
        a.Activate();
    }
}
