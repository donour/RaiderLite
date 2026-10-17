using System.Globalization;
using RaiderLite.Core.Definitions;
using RaiderLite.Core.Model;

namespace RaiderLite;

/// <summary>Base for the small fixed dialogs: OK/Cancel buttons along the bottom.</summary>
public abstract class DialogBase : Form
{
    protected readonly FlowLayoutPanel ButtonPanel = new()
    {
        Dock = DockStyle.Bottom,
        FlowDirection = FlowDirection.RightToLeft,
        AutoSize = true,
        Padding = new Padding(8),
    };

    protected DialogBase(string title, bool cancel = true)
    {
        Text = title;
        Font = UiFonts.Regular;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;

        var ok = new Button { Text = cancel ? "OK" : "Close", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 0) };
        ok.Click += (_, e) => OnOk(e);
        ButtonPanel.Controls.Add(ok);
        AcceptButton = ok;
        if (cancel)
        {
            var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(80, 0) };
            ButtonPanel.Controls.Add(cancelButton);
            CancelButton = cancelButton;
        }
        else
        {
            CancelButton = ok;
        }
        Controls.Add(ButtonPanel);
    }

    protected virtual void OnOk(EventArgs e)
    {
    }
}

/// <summary>Read-only key/value list plus a notes box (table and image properties).</summary>
public sealed class PropertiesForm : DialogBase
{
    public PropertiesForm(string title, IEnumerable<(string Key, string Value)> properties, string? notes) : base(title, cancel: false)
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        Size = new Size(620, 560);
        MinimumSize = new Size(400, 300);

        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            GridLines = true,
        };
        list.Columns.Add("Property", 170);
        list.Columns.Add("Value", 400);
        foreach (var (key, value) in properties)
            list.Items.Add(new ListViewItem([key, value]));

        // Copy selected rows with Ctrl+C.
        list.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C && list.SelectedItems.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, list.SelectedItems.Cast<ListViewItem>().Select(i => $"{i.Text}\t{i.SubItems[1].Text}")));
        };

        if (!string.IsNullOrWhiteSpace(notes))
        {
            var box = new TextBox
            {
                Dock = DockStyle.Bottom,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Height = 150,
                Text = notes.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
            };
            Controls.Add(list);
            Controls.Add(box);
        }
        else
        {
            Controls.Add(list);
        }
        list.BringToFront();
    }

    public static IEnumerable<(string, string)> DescribeTable(Table table)
    {
        yield return ("Name", table.Name);
        yield return ("Category", table.Category);
        yield return ("Type", table switch
        {
            TableBitwiseSwitch => "Bitwise switch",
            TableSwitch => "Switch",
            Table3D => "3D",
            Table2D => "2D",
            _ => "1D",
        });
        yield return ("Shape", table.ShapeDescription);
        yield return ("Storage address", $"0x{table.StorageAddress:X}");
        if (table.RamOffset != 0)
            yield return ("File offset", $"0x{table.StorageAddress - table.RamOffset:X} (RAM offset 0x{table.RamOffset:X})");
        yield return ("Storage type", StorageType.Describe(table.StorageType, table.Signed));
        yield return ("Endian", table.MemModelEndian is { } mm ? $"{mm} (memory model)" : table.Endian.ToString());
        switch (table)
        {
            case Table3D t3:
                yield return ("Size", $"{t3.SizeX} x {t3.SizeY}");
                if (t3.SwapXY || t3.FlipX || t3.FlipY || t3.SkipCells != 0)
                    yield return ("Layout", $"swapxy={t3.SwapXY} flipx={t3.FlipX} flipy={t3.FlipY} skipCells={t3.SkipCells}");
                break;
            default:
                yield return ("Size", table.DataSize.ToString(CultureInfo.InvariantCulture));
                break;
        }
        if (table.BitMask != 0)
            yield return ("Mask", $"0x{table.BitMask:X}");
        if (table.DataLayout != DataLayout.Default)
            yield return ("Data layout", table.DataLayout.ToString());
        yield return ("User level", table.UserLevel.ToString(CultureInfo.InvariantCulture));
        if (table.Locked)
            yield return ("Locked", "yes");
        if (!string.IsNullOrEmpty(table.LogParam))
            yield return ("Log parameter", table.LogParam);

        foreach (var scale in table.Scales)
        {
            var mark = ReferenceEquals(scale, table.CurrentScale) ? " (current)" : "";
            yield return ($"Scale: {scale.Category}{mark}",
                $"{scale.Unit}: {scale.Expression}  |  to_byte: {scale.ByteExpression ?? "(approximated)"}  |  format {scale.Format}  |  fine {scale.FineIncrement}, coarse {scale.CoarseIncrement}");
        }

        foreach (var axis in table.Axes)
        {
            var label = axis.AxisKind == AxisKind.Y ? "Y axis" : "X axis";
            if (axis.IsStaticDataTable)
            {
                yield return (label, $"{axis.Name} (static): {string.Join(", ", axis.StaticTexts)}");
                continue;
            }
            var s = axis.CurrentScale;
            yield return (label, $"{axis.Name} at 0x{axis.StorageAddress:X}, {StorageType.Describe(axis.StorageType, axis.Signed)}, {axis.DataSize} values" +
                                 (!s.IsRaw ? $", {s.Unit}: {s.Expression}" : ""));
        }

        if (table.Presets.Presets.Count > 0)
        {
            foreach (var p in table.Presets.Presets)
                yield return (table is TableBitwiseSwitch ? "Bit" : "State", $"{p.Name}: {p.DataText}");
        }
    }

    public static IEnumerable<(string, string)> DescribeRom(Rom rom)
    {
        var id = rom.RomId;
        yield return ("File", rom.FilePath);
        yield return ("File size", $"{rom.Binary.Length} bytes (0x{rom.Binary.Length:X})");
        yield return ("Definition", rom.DefinitionPath ?? "");
        yield return ("XML ID", id.XmlId);
        yield return ("Internal ID", $"\"{id.InternalIdString}\" at 0x{id.InternalIdAddress:X}");
        foreach (var (k, v) in new[]
                 {
                     ("Make", id.Make), ("Market", id.Market), ("Model", id.Model), ("Submodel", id.SubModel),
                     ("Transmission", id.Transmission), ("Year", id.Year), ("ECU ID", id.EcuId), ("Case ID", id.CaseId),
                     ("Memory model", id.MemModel), ("Flash method", id.FlashMethod), ("Author", id.Author), ("Version", id.Version),
                 })
        {
            if (!string.IsNullOrEmpty(v) && v != "Unknown")
                yield return (k, v);
        }
        yield return ("Definition file size", id.FileSize > 0 ? $"{id.FileSize} bytes" : "(not specified)");
        yield return ("RAM offset", id.NoRamOffset && id.RamOffset == 0 ? "none" : $"0x{id.RamOffset:X}");
        yield return ("Tables", rom.Tables.Count.ToString(CultureInfo.InvariantCulture));
        if (rom.FaultyTables.Count > 0)
            yield return ("Unreadable tables", string.Join(", ", rom.FaultyTables));
        if (rom.ChecksumManagers.Count > 0)
        {
            yield return ("Checksums", string.Join(", ", rom.ChecksumManagers.Select(c => c.Type)));
            yield return ("Valid checksums", $"{rom.ValidateChecksums()} of {rom.TotalChecksums}");
        }
        else
        {
            yield return ("Checksums", "none defined");
        }
        yield return ("Unsaved changes", rom.IsDirty ? "yes" : "no");
    }
}

/// <summary>Lists the definition files searched when an image is opened (in order).</summary>
public sealed class DefinitionManagerForm : DialogBase
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, SelectionMode = SelectionMode.MultiExtended };

    public DefinitionManagerForm(IEnumerable<string> files) : base("Definition Manager")
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        Size = new Size(700, 400);
        MinimumSize = new Size(450, 250);
        foreach (var f in files)
            _list.Items.Add(f);

        var side = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(6) };
        Button Add(string text, EventHandler click)
        {
            var b = new Button { Text = text, Width = 110 };
            b.Click += click;
            side.Controls.Add(b);
            return b;
        }
        Add("&Add...", (_, _) => AddFiles());
        Add("&Remove", (_, _) => { foreach (var item in _list.SelectedItems.Cast<object>().ToList()) _list.Items.Remove(item); });
        Add("Move &Up", (_, _) => MoveSelected(-1));
        Add("Move &Down", (_, _) => MoveSelected(1));

        var hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 36,
            Padding = new Padding(6, 6, 6, 0),
            Text = "Definitions are searched in this order; the first rom whose internal ID matches the image is used.",
        };

        Controls.Add(_list);
        Controls.Add(side);
        Controls.Add(hint);
        _list.BringToFront();
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] dropped)
                AddPaths(dropped);
        };
    }

    public List<string> Files => _list.Items.Cast<string>().ToList();

    private void AddFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add ECU Definitions",
            Filter = "RomRaider definitions (*.xml)|*.xml|All files (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = Directory.Exists(AppSettings.Current.LastDefinitionDir) ? AppSettings.Current.LastDefinitionDir : "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        AppSettings.Current.LastDefinitionDir = Path.GetDirectoryName(dialog.FileNames[0]) ?? "";
        AddPaths(dialog.FileNames);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(path, "*.xml");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    MessageBox.Show(this, ex.Message, "Add Definitions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                AddPaths(files);
                continue;
            }
            if (!_list.Items.Cast<string>().Contains(path, StringComparer.OrdinalIgnoreCase))
                _list.Items.Add(path);
        }
    }

    private void MoveSelected(int delta)
    {
        int i = _list.SelectedIndex;
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _list.Items.Count)
            return;
        var item = _list.Items[i];
        _list.Items.RemoveAt(i);
        _list.Items.Insert(j, item);
        _list.ClearSelected();
        _list.SelectedIndex = j;
    }
}

/// <summary>Picks a rom from a definition that didn't match the image (force load).</summary>
public sealed class RomPickerForm : DialogBase
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public RomPickerForm(DefinitionDocument doc, string imageName) : base("Choose ROM Definition")
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        Size = new Size(520, 380);
        foreach (var rom in doc.Roms)
            _list.Items.Add(rom);
        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;
        _list.DoubleClick += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        var label = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 52,
            Padding = new Padding(6),
            Text = $"No rom in {doc} has an internal ID matching {imageName}.\nChoose one to load the image with anyway (tables may show garbage if it is the wrong one):",
        };
        Controls.Add(_list);
        Controls.Add(label);
        _list.BringToFront();
    }

    public RomDefinitionInfo? Selected => _list.SelectedItem as RomDefinitionInfo;
}

/// <summary>Editor settings.</summary>
public sealed class SettingsForm : DialogBase
{
    private readonly AppSettings _settings;
    private readonly ComboBox _userLevel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _defaultScale = new() { Width = 200 };
    private readonly CheckBox _scaleHeaders = new() { Text = "Switch axis scaling with the table's scaling", AutoSize = true };
    private readonly CheckBox _displayHigh = new() { Text = "Show tables above the user level (read-only)", AutoSize = true };
    private readonly CheckBox _saveDebug = new() { Text = "Allow editing debug-level (5) tables", AutoSize = true };
    private readonly CheckBox _valueLimit = new() { Text = "Highlight values outside the storage range", AutoSize = true };
    private readonly CheckBox _colorAxis = new() { Text = "Colour axis cells", AutoSize = true };
    private readonly CheckBox _sortTree = new() { Text = "Sort tables alphabetically", AutoSize = true };
    private readonly CheckBox _showGraphs = new() { Text = "Show graphs when opening tables (Ctrl+G toggles)", AutoSize = true };
    private readonly CheckBox _singleClick = new() { Text = "Open and close tables with a single click (otherwise double-click)", AutoSize = true };
    private readonly NumericUpDown _cellWidth = new() { Minimum = 24, Maximum = 200, Width = 70 };
    private readonly NumericUpDown _cellHeight = new() { Minimum = 14, Maximum = 60, Width = 70 };
    private readonly Dictionary<string, Button> _colorButtons = [];

    public SettingsForm(AppSettings settings) : base("Settings")
    {
        _settings = settings;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        for (int i = 0; i < 5; i++)
            _userLevel.Items.Add($"{i + 1} - {AppSettings.UserLevelNames[i]}");
        _userLevel.SelectedIndex = Math.Clamp(settings.UserLevel - 1, 0, 4);
        _defaultScale.Text = settings.DefaultScale;
        _scaleHeaders.Checked = settings.ScaleHeadersAndData;
        _displayHigh.Checked = settings.DisplayHighTables;
        _saveDebug.Checked = settings.SaveDebugTables;
        _valueLimit.Checked = settings.ValueLimitWarning;
        _colorAxis.Checked = settings.ColorAxis;
        _sortTree.Checked = settings.SortTableTree;
        _singleClick.Checked = settings.SingleClickTables;
        _showGraphs.Checked = settings.ShowGraphsByDefault;
        _cellWidth.Value = Math.Clamp(settings.CellWidth, 24, 200);
        _cellHeight.Value = Math.Clamp(settings.CellHeight, 14, 60);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
        void Row(string label, Control control)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 3) });
            layout.Controls.Add(control);
        }
        void Full(Control control)
        {
            layout.Controls.Add(control);
            layout.SetColumnSpan(control, 2);
        }

        Row("User level", _userLevel);
        Row("Preferred scaling category", _defaultScale);
        Full(_scaleHeaders);
        Full(_displayHigh);
        Full(_saveDebug);
        Full(_valueLimit);
        Full(_sortTree);
        Full(_singleClick);
        Full(_showGraphs);
        Full(_colorAxis);
        Row("Minimum cell width", _cellWidth);
        Row("Cell height", _cellHeight);

        var colors = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(460, 0) };
        AddColor(colors, "Min", settings.MinColor);
        AddColor(colors, "Max", settings.MaxColor);
        AddColor(colors, "Selection", settings.HighlightColor);
        AddColor(colors, "Increased", settings.IncreaseBorder);
        AddColor(colors, "Decreased", settings.DecreaseBorder);
        AddColor(colors, "Axis", settings.AxisColor);
        AddColor(colors, "Warning", settings.WarningColor);
        Row("Colours", colors);

        var reset = new Button { Text = "Default Colours", AutoSize = true };
        reset.Click += (_, _) =>
        {
            var d = new AppSettings();
            SetColor("Min", d.MinColor);
            SetColor("Max", d.MaxColor);
            SetColor("Selection", d.HighlightColor);
            SetColor("Increased", d.IncreaseBorder);
            SetColor("Decreased", d.DecreaseBorder);
            SetColor("Axis", d.AxisColor);
            SetColor("Warning", d.WarningColor);
        };
        Row("", reset);

        Controls.Add(layout);
        layout.BringToFront();
    }

    private void AddColor(FlowLayoutPanel panel, string name, Color color)
    {
        var button = new Button { Text = name, Width = 84, Height = 26, Margin = new Padding(2) };
        button.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = button.BackColor, FullOpen = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                SetColor(name, dialog.Color);
        };
        _colorButtons[name] = button;
        panel.Controls.Add(button);
        SetColor(name, color);
    }

    private void SetColor(string name, Color color)
    {
        var b = _colorButtons[name];
        b.BackColor = color;
        b.ForeColor = ColorScaler.ContrastText(color);
    }

    protected override void OnOk(EventArgs e)
    {
        var s = _settings;
        s.UserLevel = _userLevel.SelectedIndex + 1;
        s.DefaultScale = _defaultScale.Text.Trim().Length > 0 ? _defaultScale.Text.Trim() : "Metric";
        s.ScaleHeadersAndData = _scaleHeaders.Checked;
        s.DisplayHighTables = _displayHigh.Checked;
        s.SaveDebugTables = _saveDebug.Checked;
        s.ValueLimitWarning = _valueLimit.Checked;
        s.ColorAxis = _colorAxis.Checked;
        s.SortTableTree = _sortTree.Checked;
        s.SingleClickTables = _singleClick.Checked;
        s.ShowGraphsByDefault = _showGraphs.Checked;
        s.CellWidth = (int)_cellWidth.Value;
        s.CellHeight = (int)_cellHeight.Value;
        s.MinColor = _colorButtons["Min"].BackColor;
        s.MaxColor = _colorButtons["Max"].BackColor;
        s.HighlightColor = _colorButtons["Selection"].BackColor;
        s.IncreaseBorder = _colorButtons["Increased"].BackColor;
        s.DecreaseBorder = _colorButtons["Decreased"].BackColor;
        s.AxisColor = _colorButtons["Axis"].BackColor;
        s.WarningColor = _colorButtons["Warning"].BackColor;
    }
}

/// <summary>Lists the tables that differ between two open images (RomRaider's Compare Images).</summary>
public sealed class CompareImagesForm : Form
{
    private static readonly Color EqualColor = Color.FromArgb(52, 114, 53);
    private static readonly Color DifferentColor = Color.FromArgb(193, 27, 23);
    private static readonly Color MissingColor = Color.FromArgb(200, 140, 0);

    private readonly MainForm _main;
    private readonly ComboBox _left = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly ComboBox _right = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly CheckBox _differencesOnly = new() { Text = "Differences only", AutoSize = true, Checked = true };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly Label _summary = new() { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };

    public CompareImagesForm(MainForm main, IReadOnlyList<Rom> roms)
    {
        _main = main;
        Text = "Compare Images";
        Font = UiFonts.Regular;
        Size = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;
        ShowIcon = false;
        ShowInTaskbar = false;

        foreach (var r in roms)
        {
            _left.Items.Add(r);
            _right.Items.Add(r);
        }
        _left.SelectedIndex = 0;
        _right.SelectedIndex = Math.Min(1, roms.Count - 1);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
        top.Controls.Add(_left);
        top.Controls.Add(new Label { Text = "vs.", AutoSize = true, Margin = new Padding(6, 7, 6, 0) });
        top.Controls.Add(_right);
        var compare = new Button { Text = "Compare", AutoSize = true };
        compare.Click += (_, _) => Compare();
        top.Controls.Add(compare);
        top.Controls.Add(_differencesOnly);

        _list.Columns.Add("Table", 380);
        _list.Columns.Add("Category", 160);
        _list.Columns.Add("Result", 160);
        _list.DoubleClick += (_, _) => OpenSelected();

        var hint = new Label { Dock = DockStyle.Bottom, Height = 20, Text = "Double-click a table to open both versions side by side.", Padding = new Padding(6, 0, 0, 0), ForeColor = SystemColors.GrayText };
        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(hint);
        Controls.Add(_summary);
        _list.BringToFront();
        _differencesOnly.CheckedChanged += (_, _) => Compare();
        Shown += (_, _) => Compare();
    }

    private void Compare()
    {
        if (_left.SelectedItem is not Rom a || _right.SelectedItem is not Rom b)
            return;
        if (!_main.OpenRoms.Contains(a) || !_main.OpenRoms.Contains(b))
        {
            // An image was closed since this window opened.
            _list.Items.Clear();
            _summary.Text = "One of these images has been closed.";
            return;
        }
        _list.BeginUpdate();
        _list.Items.Clear();
        int same = 0, different = 0, missing = 0;
        foreach (var ta in a.Tables)
        {
            var tb = b.GetTable(ta.Name);
            string result;
            Color color;
            if (tb == null)
            {
                result = $"Missing in {b.FileName}";
                color = MissingColor;
                missing++;
            }
            else if (!ta.IsCompatibleWith(tb))
            {
                result = "Different size";
                color = DifferentColor;
                different++;
            }
            else if (ta.AllCells.Zip(tb.AllCells).All(p => p.First.IsStatic || p.First.BinValue == p.Second.BinValue))
            {
                result = "Same";
                color = EqualColor;
                same++;
                if (_differencesOnly.Checked)
                    continue;
            }
            else
            {
                result = "Different";
                color = DifferentColor;
                different++;
            }
            _list.Items.Add(new ListViewItem([ta.Name, ta.Category, result]) { ForeColor = color, Tag = (ta, tb) });
        }
        foreach (var tb in b.Tables.Where(t => a.GetTable(t.Name) == null))
        {
            missing++;
            _list.Items.Add(new ListViewItem([tb.Name, tb.Category, $"Missing in {a.FileName}"]) { ForeColor = MissingColor, Tag = ((Table?)null, tb) });
        }
        _list.EndUpdate();
        _summary.Text = $"{same} same, {different} different, {missing} missing in one image.";
    }

    private void OpenSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not ValueTuple<Table?, Table?> pair)
            return;
        var (left, right) = pair;
        // Don't open tables of an image that has since been closed.
        if (left?.Rom is { } lr && !_main.OpenRoms.Contains(lr))
            left = null;
        if (right?.Rom is { } rr && !_main.OpenRoms.Contains(rr))
            right = null;
        if (left == null && right == null)
        {
            MessageBox.Show(this, "That image has been closed.", "Compare Images", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (left != null && right != null && left.IsCompatibleWith(right))
            _main.OpenComparison(left, right);
        else if (left != null)
            _main.OpenTable(left);
        else if (right != null)
            _main.OpenTable(right);
    }
}
