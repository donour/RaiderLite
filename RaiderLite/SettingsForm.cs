namespace RaiderLite;

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
    private readonly TextBox _tuneLibrary = new() { Width = 300 };
    private readonly Dictionary<string, Button> _colorButtons = [];
    private readonly ToolTip _tips = new();

    public SettingsForm(AppSettings settings) : base("Settings")
    {
        HelpTopic = "settings";
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

        _tuneLibrary.Text = settings.TuneLibraryFolder;
        var browse = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(3, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose the tune library folder: each car is a folder in it.",
                UseDescriptionForTitle = true,
                InitialDirectory = Directory.Exists(_tuneLibrary.Text) ? _tuneLibrary.Text : "",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _tuneLibrary.Text = dialog.SelectedPath;
        };
        var library = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        library.Controls.Add(_tuneLibrary);
        library.Controls.Add(browse);
        Row("Tune library folder", library);

        var colors = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(460, 0) };
        AddColor(colors, "Min", settings.MinColor);
        AddColor(colors, "Max", settings.MaxColor);
        AddColor(colors, "Selection", settings.HighlightColor);
        AddColor(colors, "Increased", settings.IncreaseBorder);
        AddColor(colors, "Decreased", settings.DecreaseBorder);
        AddColor(colors, "Axis", settings.AxisColor);
        AddColor(colors, "Warning", settings.WarningColor);
        Row("Colours", colors);

        var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var reset = new Button { Text = "Default Colours", AutoSize = true };
        reset.Click += (_, _) => ApplyPreset(ColorPreset.Default);
        var colorBlind = new Button { Text = "Colour-blind Friendly", AutoSize = true };
        colorBlind.Click += (_, _) => ApplyPreset(ColorPreset.ColorBlind);
        _tips.SetToolTip(colorBlind, "Light orange to dark red shading, and blue, sky blue, yellow and black for the rest:\n" +
                                     "colours that stay distinct with red-green or blue-yellow colour blindness.");
        presets.Controls.Add(reset);
        presets.Controls.Add(colorBlind);
        Row("", presets);

        Controls.Add(layout);
        layout.BringToFront();
    }

    private void AddColor(FlowLayoutPanel panel, string name, Color color)
    {
        var button = new Button { Text = name, AutoSize = true, MinimumSize = new Size(84, 26), Margin = new Padding(2) };
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

    private void ApplyPreset(ColorPreset preset)
    {
        SetColor("Min", preset.Min);
        SetColor("Max", preset.Max);
        SetColor("Selection", preset.Selection);
        SetColor("Increased", preset.Increased);
        SetColor("Decreased", preset.Decreased);
        SetColor("Axis", preset.Axis);
        SetColor("Warning", preset.Warning);
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
        // The default library is stored as empty, so it follows the Documents folder.
        var library = _tuneLibrary.Text.Trim();
        s.TuneLibrary = library.Length == 0 || string.Equals(library, AppSettings.DefaultTuneLibrary, StringComparison.OrdinalIgnoreCase) ? "" : library;
        s.MinColor = _colorButtons["Min"].BackColor;
        s.MaxColor = _colorButtons["Max"].BackColor;
        s.HighlightColor = _colorButtons["Selection"].BackColor;
        s.IncreaseBorder = _colorButtons["Increased"].BackColor;
        s.DecreaseBorder = _colorButtons["Decreased"].BackColor;
        s.AxisColor = _colorButtons["Axis"].BackColor;
        s.WarningColor = _colorButtons["Warning"].BackColor;
    }
}
