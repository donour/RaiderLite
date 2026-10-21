using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using RaiderLite.Core.Definitions;
using RaiderLite.Core.Layout;
using RaiderLite.Core.Model;

namespace RaiderLite;

/// <summary>The editor window: ROM/table tree on the left, table windows in the MDI area.</summary>
public sealed partial class MainForm : Form
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
    private readonly TextBox _filter = new() { Dock = DockStyle.Top, PlaceholderText = "Filter tables (Ctrl+F)", AccessibleName = "Filter tables" };
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
    private readonly MdiClient _mdiClient;
    private TableForm? _activeTable;

    private readonly Panel _tablesPage = new() { Dock = DockStyle.Fill };
    private readonly TunesPanel _tunesPanel;
    private readonly RadioButton _tablesTab = PanelTab("Tables");
    private readonly RadioButton _tunesTab = PanelTab("Tunes");
    private bool _carsListed;

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
        _tablesPage.Controls.Add(_tree);
        _tablesPage.Controls.Add(_filter);
        _tunesPanel = new TunesPanel(this, _cars) { Dock = DockStyle.Fill, Visible = false };
        _treePanel.Controls.Add(_tablesPage);
        _treePanel.Controls.Add(_tunesPanel);
        _treePanel.Controls.Add(BuildPanelTabs());

        // The locked-revision bar: a lock, what is locked and why, and the way to change it.
        _lockBar.Height = LogicalToDeviceUnits(34);
        _lockBar.Padding = new Padding(LogicalToDeviceUnits(8), LogicalToDeviceUnits(4), LogicalToDeviceUnits(8), LogicalToDeviceUnits(4));
        var lockIcon = new PictureBox
        {
            Dock = DockStyle.Left,
            Width = LogicalToDeviceUnits(24),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = IconFont.Render(IconFont.Lock, LogicalToDeviceUnits(16), Color.FromArgb(120, 90, 0)),
        };
        _lockBar.Controls.Add(_lockText);
        _lockBar.Controls.Add(lockIcon);
        _lockBar.Controls.Add(_lockNewRevision);
        _lockNewRevision.Click += async (_, _) =>
        {
            if (_lockBarRevision != null)
                await NewRevisionAsync(_lockBarRevision);
        };

        // Dock order: added last docks first.
        Controls.Add(_splitter);
        Controls.Add(_treePanel);
        Controls.Add(_status);
        Controls.Add(_lockBar);
        Controls.Add(Toolbar);
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        // IsMdiContainer created the client area.
        _mdiClient = Controls.OfType<MdiClient>().First();
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
        ShowLeftPanel(tunes: AppSettings.Current.LeftPanel == "Tunes");
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
    public Size MdiClientArea => _mdiClient.ClientSize;

    // ----- menus -----

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("&Open Image...", null, async (_, _) => await OpenImageDialogAsync(), Keys.Control | Keys.O));
        file.DropDownItems.Add(_recentMenu);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("New &Revision...", null, async (_, _) => await NewRevisionAsync(), Keys.Control | Keys.Shift | Keys.N));
        file.DropDownItems.Add(new ToolStripMenuItem("New Ca&r...", null, (_, _) => NewCar()));
        file.DropDownItems.Add(new ToolStripMenuItem("&Import Image as New Car...", null, (_, _) => ImportImage(SelectedRom)));
        file.DropDownItems.Add(new ToolStripSeparator());
        // Items that act on the selected image, and Close All, which needs any image open.
        ToolStripMenuItem[] imageItems =
        [
            new("&Save Image", null, (_, _) => SaveRom(SelectedRom, saveAs: false), Keys.Control | Keys.S),
            new("Save Image &As...", null, (_, _) => SaveRom(SelectedRom, saveAs: true), Keys.Control | Keys.Shift | Keys.S),
            new("&Close Image", null, (_, _) => CloseRom(SelectedRom)),
            new("Image &Properties...", null, (_, _) => ShowRomProperties(SelectedRom)),
        ];
        var closeAll = new ToolStripMenuItem("Close All &Images", null, (_, _) => CloseAllRoms());
        file.DropDownItems.Add(imageItems[0]);
        file.DropDownItems.Add(imageItems[1]);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(imageItems[2]);
        file.DropDownItems.Add(closeAll);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(imageItems[3]);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));
        file.DropDownOpening += (_, _) =>
        {
            bool selected = SelectedRom != null;
            foreach (var item in imageItems)
                item.Enabled = selected;
            closeAll.Enabled = _roms.Count > 0;
        };
        // Enabled again once the menu closes, as a disabled item's shortcut (Ctrl+S) wouldn't work.
        file.DropDownClosed += (_, _) =>
        {
            foreach (var item in imageItems)
                item.Enabled = true;
            closeAll.Enabled = true;
        };

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
        view.DropDownItems.Add(new ToolStripMenuItem("&Find Table", null, (_, _) => FocusFilter(), Keys.Control | Keys.F));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(new ToolStripMenuItem("T&unes", null, (_, _) => ShowLeftPanel(tunes: true)));
        view.DropDownItems.Add(new ToolStripMenuItem("&Tables", null, (_, _) => ShowLeftPanel(tunes: false)));

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add(new ToolStripMenuItem("&Compare Images...", null, (_, _) => ShowCompareImages()));

        var window = new ToolStripMenuItem("&Window");
        // Each layout starts from every table's fitted size, unlike the stock MDI layouts, which
        // give every window the same size whatever its table.
        window.DropDownItems.Add(new ToolStripMenuItem("&Arrange", null, (_, _) => ArrangeTables(WindowArrangement.Arrange)) { ToolTipText = "Each table at the size that shows it, in whichever layout shows the most of the tables" });
        window.DropDownItems.Add(new ToolStripMenuItem("Tile &Side by Side", null, (_, _) => ArrangeTables(WindowArrangement.SideBySide)) { ToolTipText = "One column per table, as wide as it needs" });
        window.DropDownItems.Add(new ToolStripMenuItem("Tile S&tacked", null, (_, _) => ArrangeTables(WindowArrangement.Stacked)) { ToolTipText = "One row per table, as tall as it needs" });
        window.DropDownItems.Add(new ToolStripMenuItem("&Cascade", null, (_, _) => ArrangeTables((sizes, area, min) =>
            WindowArrangement.Cascade(sizes, area, min, SystemInformation.CaptionHeight + LogicalToDeviceUnits(8)))));
        window.DropDownItems.Add(new ToolStripSeparator());
        window.DropDownItems.Add(new ToolStripMenuItem("Close &All Tables", null, (_, _) => CloseAllTables()));
        window.DropDownItems.Add(new ToolStripSeparator());
        _menu.MdiWindowListItem = window;

        var help = new ToolStripMenuItem("&Help");
        // F1 itself is handled in ProcessCmdKey: it opens the page about what has the focus.
        help.DropDownItems.Add(new ToolStripMenuItem("RaiderLite &Help", null, (_, _) => HelpForm.ShowTopic("overview")) { ShortcutKeyDisplayString = "F1" });
        help.DropDownItems.Add(new ToolStripMenuItem("&Keyboard Shortcuts", null, (_, _) => HelpForm.ShowTopic("keyboard-shortcuts")));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(new ToolStripMenuItem("&About RaiderLite", null, (_, _) => ShowAbout()));

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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Table windows pass their keys up here too.
        if (keyData == Keys.F1)
        {
            HelpForm.ShowTopic(HelpContext());
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>The help page about what has the focus (or is in front).</summary>
    private string HelpContext()
    {
        if (_tunesPanel.Visible && _tunesPanel.ContainsFocus)
            return "tunes";
        if (_treePanel.ContainsFocus)
            return "table-list";
        if (ActiveMdiChild is TableForm form)
            return form.HelpTopic;
        return _tunesPanel.Visible ? "tunes" : "overview";
    }

    private void ShowAbout()
    {
        // Version and copyright come from Directory.Build.props via the generated assembly attributes.
        var version = Application.ProductVersion.Split('+')[0];
        var copyright = typeof(MainForm).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;
        var page = new TaskDialogPage
        {
            Caption = "About RaiderLite",
            Heading = $"RaiderLite {version}",
            Icon = TaskDialogIcon.Information,
            EnableLinks = true,
            AllowCancel = true,
            Text = $"""
                {copyright}

                An ECU map editor using RomRaider XML definitions: a C# port of RomRaider's table editor (editing only).

                Thank you to the RomRaider developers and community, whose many years of work on RomRaider made this project possible.
                <a href="https://www.romraider.com">www.romraider.com</a>   <a href="https://github.com/RomRaider/RomRaider">github.com/RomRaider/RomRaider</a>
                RomRaider is Copyright © 2006-2025 RomRaider.com.
                """,
            Footnote = new TaskDialogFootnote(
                "This program is free software, licensed under the GNU General Public License version 2. " +
                "It comes with ABSOLUTELY NO WARRANTY. See LICENSE.txt in the installation folder for details."),
            Buttons = { TaskDialogButton.OK },
        };
        page.LinkClicked += (_, e) =>
        {
            if (e.LinkHref.StartsWith("https://", StringComparison.Ordinal))
                Process.Start(new ProcessStartInfo(e.LinkHref) { UseShellExecute = true });
        };
        TaskDialog.ShowDialog(this, page);
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
        _tunesPanel.CommitEdits();
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

    internal void FocusFilter()
    {
        ShowLeftPanel(tunes: false);
        _filter.Focus();
    }

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
        UpdateTitle();
        if (_tree.SelectedNode?.Tag is Table t)
        {
            var scale = t.CurrentScale;
            SetStatus($"{t.Name}  |  {t.ShapeDescription}  |  0x{t.StorageAddress:X}  |  {StorageType.Describe(t.StorageType, t.Signed)}" +
                      (!scale.IsRaw ? $"  |  {scale.Unit}" : ""));
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
            parts.Add($"{ValueFormatter.Format(cell.RealValue, scale.Format)} {(scale.IsRaw ? "" : scale.Unit)}".Trim());
            parts.Add($"raw {cell.BinValue.ToString(CultureInfo.CurrentCulture)} (0x{(long)cell.BinValue & 0xFFFFFFFF:X})");
            parts.Add($"@0x{cell.Address:X}");
        }

        var selected = form.Table.SelectedCells.Where(c => !c.IsStatic).ToList();
        if (selected.Count > 1)
        {
            var values = selected.Select(c => c.RealValue).ToList();
            var fmt = form.Table.CurrentScale.Format;
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
        return $"{ValueFormatter.Format(cell.RealValue, scale.Format)} {(scale.IsRaw ? "" : scale.Unit)}".Trim();
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

    /// <summary>
    /// Opens an image, finding its definition; <paramref name="preferredXmlId"/> wins among several
    /// matches (a revision's car knows its definition). Returns the rom, or null if not opened.
    /// </summary>
    public async Task<Rom?> OpenImageAsync(string path, string? preferredXmlId = null)
    {
        if (_roms.Any(r => string.Equals(r.FilePath, path, StringComparison.OrdinalIgnoreCase)))
        {
            if (MessageBox.Show(this, $"{Path.GetFileName(path)} is already open. Open another copy?", "Open Image",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return null;
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
                return null;
            }
            image = await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Open Image", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }

        UseWaitCursor = true;
        SetStatus($"Finding a definition for {Path.GetFileName(path)}...");
        try
        {
            var definitionFiles = AppSettings.Current.DefinitionFiles.ToList();
            var errors = new List<string>();
            // Every definition is checked: the first match isn't necessarily the right one when one
            // ID is a prefix of another, and a wrong definition would edit the wrong addresses.
            var matches = await Task.Run(() => FindDefinitionMatches(definitionFiles, image, errors));
            UseWaitCursor = false;
            foreach (var error in errors)
                SetStatus(error);

            int preferred = preferredXmlId == null
                ? -1
                : matches.FindIndex(m => string.Equals(m.Info.Id.XmlId, preferredXmlId, StringComparison.OrdinalIgnoreCase));
            (DefinitionDocument Doc, RomDefinitionInfo Info)? chosen = matches.Count > 0 ? matches[Math.Max(0, preferred)] : null;
            chosen ??= ChooseDefinitionManually(path, image, errors);
            if (chosen is not { } c)
            {
                SetStatus("Open cancelled.");
                return null;
            }

            UseWaitCursor = true;
            SetStatus($"Loading {Path.GetFileName(path)} with {c.Info.Id.XmlId}...");
            var rom = await Task.Run(() => RomUnmarshaller.Load(c.Doc, c.Info.Node, image, path));
            foreach (var (doc, info) in matches.Where(m => !ReferenceEquals(m.Info, c.Info)))
            {
                rom.Warnings.Add(preferred >= 0
                    ? $"This image also matches {info.Id.XmlId} ({Path.GetFileName(doc.Path)}); {c.Info.Id.XmlId} was used because it is the definition of the car this revision belongs to."
                    : info.Id.MatchLength == c.Info.Id.MatchLength
                    ? $"This image also matches {info.Id.XmlId} ({Path.GetFileName(doc.Path)}), equally specifically; {c.Info.Id.XmlId} was used because it comes first in the definition list. Check it is the right one."
                    : $"This image also matches {info.Id.XmlId} ({Path.GetFileName(doc.Path)}); {c.Info.Id.XmlId} was used because its ID is more specific.");
            }
            AddRom(rom);
            AppSettings.Current.AddRecentFile(path);
            UpdateRecentMenu();
            ReportLoad(rom);
            return rom;
        }
        catch (DefinitionException ex)
        {
            MessageBox.Show(this, ex.Message, $"Error loading {Path.GetFileName(path)}", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Error loading image.");
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Anything else from a malformed definition or image: report it, don't take the editor down.
            ErrorReporter.Log(ex);
            MessageBox.Show(this, $"{Path.GetFileName(path)} could not be loaded:\n\n{ex.GetType().Name}: {ex.Message}",
                "Error Loading Image", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Error loading image.");
            return null;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// <summary>
    /// The definitions in <paramref name="definitionFiles"/> that match <paramref name="image"/>, most
    /// specific first (definition list order otherwise); problems go to <paramref name="errors"/>.
    /// </summary>
    private List<(DefinitionDocument Doc, RomDefinitionInfo Info)> FindDefinitionMatches(IReadOnlyList<string> definitionFiles, byte[] image, List<string> errors)
    {
        var found = new List<(DefinitionDocument Doc, RomDefinitionInfo Info)>();
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
                found.AddRange(doc.FindMatches(image).Select(info => (doc, info)));
            }
            catch (DefinitionException ex)
            {
                errors.Add(ex.Message);
            }
        }
        return found.OrderByDescending(m => m.Info.Id.MatchLength).ToList();
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
        if (_cars.FindRevision(rom.FilePath) is { } revision)
            _romRevisions[rom] = revision;
        _tunesPanel.RefreshMarks();
        // A table's header says when its image is read-only; the lock bar offers a way out.
        rom.ReadOnlyChanged += (_, _) =>
        {
            foreach (var form in _openTables.Values.Where(f => f.Rom == rom))
                form.ApplySettings();
            UpdateTitle();
        };
        RefreshRevisionLocks();
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

        // A locked revision (stock, or with revisions based on it) keeps its image.
        var revision = _cars.FindRevision(path);
        if (revision != null && revision.Car.IsLocked(revision))
        {
            if (MessageBox.Show(this, $"{revision} ({revision.Car.Name}) is locked because {WhyLocked(revision)}, so its image can't be changed.\n\nSave your changes to another file instead?",
                    "Revision Locked", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                return SaveRom(rom, saveAs: true);
            return false;
        }

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
            SafeFile.WriteAllBytes(path, bytes);
            rom.FilePath = path;
            if (revision != null)
            {
                _romRevisions[rom] = revision;
                try
                {
                    revision.Car.ImageSaved(revision);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The image is saved; only the car's record of it is behind.
                    MessageBox.Show(this, $"The image was saved, but {revision.Car.Name}'s index couldn't be updated:\n\n{ex.Message}",
                        "Save Image", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                SuggestNotes(revision, rom);
                _tunesPanel.OnImageSaved(revision);
            }
            else
            {
                _romRevisions.Remove(rom);
            }
            RefreshRevisionLocks();
            if (revision == null)
                rom.ReadOnlyReason = null;
            rom.IsDirty = false;
            AppSettings.Current.AddRecentFile(path);
            UpdateRecentMenu();
            UpdateRomNode(rom);
            foreach (var form in _openTables.Values.Where(f => f.Rom == rom))
                form.Text = $"{form.Table.Name} - {ImageName(rom)}";
            SetStatus($"Saved {path}" + (rom.TotalChecksums > 0 ? $" ({checksums} checksum(s) updated)." : "."));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SafeFile leaves the original image untouched when a write fails.
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
        _romRevisions.Remove(rom);
        _tunesPanel.RefreshMarks();
        RebuildTree();
        UpdateUndoMenu();
        UpdateTitle();
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

    /// <summary>
    /// Lays out the open table windows (minimized ones stay minimized) from their fitted sizes.
    /// Windows keep their stacking order, with the active one on top.
    /// </summary>
    private void ArrangeTables(Func<IReadOnlyList<Size>, Size, Size, IReadOnlyList<Rectangle>> layout)
    {
        var active = ActiveMdiChild;
        var forms = MdiChildren.OfType<TableForm>().Where(f => f.Visible && f.WindowState != FormWindowState.Minimized).ToList();
        var area = _mdiClient.ClientSize;
        if (forms.Count == 0 || area.Width <= 0 || area.Height <= 0)
            return;
        // The active window goes last, so cascading leaves it on top.
        if (active is TableForm current && forms.Remove(current))
            forms.Add(current);
        var rects = layout(forms.Select(f => f.FittedWindowSize()).ToList(), area, new Size(LogicalToDeviceUnits(240), LogicalToDeviceUnits(180)));
        SuspendLayout();
        try
        {
            for (int i = 0; i < forms.Count; i++)
            {
                var form = forms[i];
                if (form.WindowState != FormWindowState.Normal)
                    form.WindowState = FormWindowState.Normal;
                if (form.FixedSize)
                    form.Location = rects[i].Location;
                else
                    form.Bounds = rects[i];
                form.BringToFront();
            }
        }
        finally
        {
            ResumeLayout();
        }
        active?.Activate();
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
        // Undo writes values straight back, past the read-only check of an edit.
        if (rom.ReadOnlyReason != null)
        {
            OnEditBlocked(rom);
            return;
        }
        var name = rom.History.UndoName;
        rom.History.Undo();
        SetStatus($"Undid {name}.");
        UpdateUndoMenu();
    }

    public void Redo(Rom? rom)
    {
        if (rom?.History.CanRedo != true)
            return;
        if (rom.ReadOnlyReason != null)
        {
            OnEditBlocked(rom);
            return;
        }
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

    // A revision shows as "037 Race TC slip target", which says more than its file name.
    private string RomNodeText(Rom rom) => (rom.IsDirty ? "* " : "") +
        (_romRevisions.TryGetValue(rom, out var revision) ? $"{revision}  [{rom.RomId.XmlId}]" : $"{rom.FileName}  [{rom.RomId.XmlId}]");

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
        UpdateTitle();
    }

    /// <summary>"RaiderLite — car › revision" when the current image is a revision; * while anything is unsaved.</summary>
    private void UpdateTitle()
    {
        var rom = SelectedRom;
        var title = rom != null && _romRevisions.TryGetValue(rom, out var revision)
            ? $"RaiderLite — {revision.Car.Name} › {revision}"
            : "RaiderLite";
        if (_roms.Any(r => r.IsDirty))
            title += " *";
        Text = title;
        UpdateLockBar(rom);
    }

    /// <summary>Shows the bar under the ribbon while the current image is a locked revision.</summary>
    private void UpdateLockBar(Rom? rom)
    {
        _lockBarRevision = rom?.ReadOnlyReason != null ? RevisionOf(rom) : null;
        if (_lockBarRevision is { } revision)
        {
            _lockText.Text = $"{revision} is locked because {WhyLocked(revision)}. To change it, make a new revision from it.";
            _lockNewRevision.Text = $"New Revision from {revision.Number:000}...";
        }
        _lockBar.Visible = _lockBarRevision != null;
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
                if (RevisionOf(rom) is { } revision)
                {
                    menu.Items.Add("New &Revision...", null, async (_, _) => await NewRevisionAsync(revision));
                    menu.Items.Add("Show in &Tunes", null, (_, _) => ShowRevisionInTunes(rom));
                    menu.Items.Add(new ToolStripSeparator());
                }
                else
                {
                    menu.Items.Add("&Import as New Car...", null, (_, _) => ImportImage(rom));
                    menu.Items.Add(new ToolStripSeparator());
                }
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
        UpdateTitle();
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
        using var dialog = new PropertiesForm($"Table Properties - {table.Name}", PropertiesForm.DescribeTable(table), table.Description) { HelpTopic = "table-list#table-properties" };
        dialog.ShowDialog(this);
    }

    internal void ShowRomProperties(Rom? rom)
    {
        if (rom == null)
            return;
        var notes = rom.Warnings.Count == 0 ? "No load warnings." : "Load warnings:" + Environment.NewLine + string.Join(Environment.NewLine, rom.Warnings);
        using var dialog = new PropertiesForm($"Image Properties - {rom.FileName}", PropertiesForm.DescribeRom(rom), notes) { HelpTopic = "images#properties" };
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
        var library = AppSettings.Current.TuneLibraryFolder;
        using var dialog = new SettingsForm(AppSettings.Current);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        if (_carsListed && !string.Equals(library, AppSettings.Current.TuneLibraryFolder, StringComparison.OrdinalIgnoreCase))
            _tunesPanel.ReloadCars();
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

    // ----- tunes -----

    private static RadioButton PanelTab(string text)
    {
        var tab = new RadioButton
        {
            Text = text,
            Appearance = Appearance.Button,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0),
            BackColor = Color.FromArgb(236, 238, 241),
        };
        tab.FlatAppearance.BorderSize = 0;
        tab.FlatAppearance.CheckedBackColor = SystemColors.Window;
        return tab;
    }

    private TableLayoutPanel BuildPanelTabs()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Height = LogicalToDeviceUnits(28), Margin = new Padding(0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        bar.Controls.Add(_tunesTab, 0, 0);
        bar.Controls.Add(_tablesTab, 1, 0);
        _tunesTab.CheckedChanged += (_, _) =>
        {
            if (_tunesTab.Checked)
                ShowLeftPanel(tunes: true);
        };
        _tablesTab.CheckedChanged += (_, _) =>
        {
            if (_tablesTab.Checked)
                ShowLeftPanel(tunes: false);
        };
        return bar;
    }

    /// <summary>Shows the Tunes or the Tables tab of the left panel.</summary>
    internal void ShowLeftPanel(bool tunes)
    {
        _tunesTab.Checked = tunes;
        _tablesTab.Checked = !tunes;
        _tunesTab.Font = tunes ? UiFonts.Bold : UiFonts.Regular;
        _tablesTab.Font = tunes ? UiFonts.Regular : UiFonts.Bold;
        _tunesPanel.Visible = tunes;
        _tablesPage.Visible = !tunes;
        AppSettings.Current.LeftPanel = tunes ? "Tunes" : "Tables";
        if (tunes && !_carsListed)
        {
            _carsListed = true;
            _tunesPanel.ReloadCars();
        }
    }

    /// <summary>Opens a table in two images side by side, comparing the first against the second.</summary>
    public void OpenComparison(Table left, Table right)
    {
        var a = OpenTable(left);
        var b = OpenTable(right);
        a.SetCompare(right);
        var area = _mdiClient.ClientSize;
        a.Location = new Point(0, 0);
        b.Location = new Point(Math.Min(a.Width + 4, Math.Max(0, area.Width - b.Width)), 0);
        a.Activate();
    }
}
