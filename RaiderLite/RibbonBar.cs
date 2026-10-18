using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using RaiderLite.Core.Model;

namespace RaiderLite;

/// <summary>
/// Ribbon-style command bar: labelled groups of large and small icon buttons. Replaces the
/// main toolbar and RomRaider's table toolbar; the table commands act on the active table window.
/// </summary>
public sealed class RibbonBar : Panel
{
    private static readonly Color Back = Color.FromArgb(246, 247, 249);
    private static readonly Color Line = Color.FromArgb(218, 220, 224);
    private static readonly Color IconColor = Color.FromArgb(50, 54, 62);
    private static readonly Color Accent = Color.FromArgb(0, 103, 192);
    private static readonly Color Green = Color.FromArgb(16, 124, 16);
    private static readonly Color Red = Color.FromArgb(196, 43, 28);
    private static readonly Color Purple = Color.FromArgb(118, 64, 168);

    private readonly MainForm _main;
    private readonly FlowLayoutPanel _groups = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Location = Point.Empty, Padding = new Padding(4, 3, 4, 0), BackColor = Back };
    private readonly ToolStripRenderer _renderer = new RibbonRenderer();
    private readonly float _dpi;

    private static readonly Color LabelColor = Color.FromArgb(96, 100, 108);

    // Adjust group: compact segmented fields (created in BuildAdjustGroup, which knows the DPI).
    private StepField _fineField;
    private StepField _coarseField;
    private StepField _valueField;

    private TextBox _fine => _fineField.Box;
    private TextBox _coarse => _coarseField.Box;
    private TextBox _value => _valueField.Box;
    private readonly ToolStripComboBox _scale = new() { DropDownStyle = ComboBoxStyle.DropDownList, ToolTipText = "Scaling of the table's values" };
    private readonly ToolStripComboBox _xScale = new() { DropDownStyle = ComboBoxStyle.DropDownList, ToolTipText = "Scaling of the X axis (the axis of a 2D table)" };
    private readonly ToolStripComboBox _yScale = new() { DropDownStyle = ComboBoxStyle.DropDownList, ToolTipText = "Scaling of the Y axis" };
    private ToolStripLabel _xScaleLabel;
    private ToolStripLabel _yScaleLabel;

    private readonly List<ToolStripItem> _romItems = [];
    private readonly List<ToolStripItem> _tableItems = [];
    private readonly List<ToolStripItem> _gridItems = [];
    private ToolStripButton _undo;
    private ToolStripButton _redo;
    private ToolStripButton _showChanges;
    private ToolStripButton _graph;
    private ToolStripButton _rescale;
    private TableForm? _form;
    private bool _binding;

    public RibbonBar(MainForm main)
    {
        _main = main;
        _dpi = main.DeviceDpi / 96f;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Back;
        Padding = new Padding(0, 0, 0, 1);
        Controls.Add(_groups);

        BuildImageGroup();
        BuildEditGroup();
        BuildAdjustGroup();
        BuildInterpolateGroup();
        BuildAxisGroup();
        BuildViewGroup();
        BuildToolsGroup();

        _fine.Leave += (_, _) => StoreIncrements();
        _coarse.Leave += (_, _) => StoreIncrements();
        _fine.KeyDown += IncrementBoxKeyDown;
        _coarse.KeyDown += IncrementBoxKeyDown;
        _value.KeyDown += ValueKeyDown;
        _scale.SelectedIndexChanged += (_, _) => ScaleSelected(_scale, _form?.Table);
        _xScale.SelectedIndexChanged += (_, _) => ScaleSelected(_xScale, XAxis(_form));
        _yScale.SelectedIndexChanged += (_, _) => ScaleSelected(_yScale, (_form?.Table as Table3D)?.YAxis);

        EqualizeGroupHeights();
        Bind(null);
        UpdateState(null);
    }

    /// <summary>Gives every group the same content height so the captions line up.</summary>
    private void EqualizeGroupHeights()
    {
        var groups = _groups.Controls.OfType<RibbonGroup>().ToList();
        int height = groups.Max(g => g.Content.GetPreferredSize(Size.Empty).Height);
        foreach (var g in groups)
            g.Content.MinimumSize = new Size(0, height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Line);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }

    private int Px(float v) => (int)Math.Round(v * _dpi);

    // ----- groups -----

    private void BuildImageGroup()
    {
        var g = AddGroup("Image");
        var big = Strip(large: true);
        big.Items.Add(Large("Open", IconFont.Open, "Open an image (Ctrl+O)", async () => await _main.OpenImageDialogAsync(), Accent));
        big.Items.Add(Large("Save", IconFont.Save, "Save the image (Ctrl+S)", () => _main.SaveRom(_main.CurrentRom, saveAs: false), Accent, _romItems));
        g.Controls.Add(big);
        g.Controls.Add(Column(
            Small("Save As", IconFont.SaveAs, "Save the image under a new name (Ctrl+Shift+S)", () => _main.SaveRom(_main.CurrentRom, saveAs: true), _romItems),
            Small("Close", IconFont.Close, "Close the image", () => _main.CloseRom(_main.CurrentRom), _romItems),
            Small("Properties", IconFont.Info, "Image properties and load warnings", () => _main.ShowRomProperties(_main.CurrentRom), _romItems)));
    }

    [MemberNotNull(nameof(_undo), nameof(_redo))]
    private void BuildEditGroup()
    {
        var g = AddGroup("Edit");
        var big = Strip(large: true);
        _undo = Large("Undo", IconFont.Undo, "Undo (Ctrl+Z)", () => _main.Undo(_main.CurrentRom), IconColor);
        _redo = Large("Redo", IconFont.Redo, "Redo (Ctrl+Y)", () => _main.Redo(_main.CurrentRom), IconColor);
        big.Items.Add(_undo);
        big.Items.Add(_redo);
        g.Controls.Add(big);
        g.Controls.Add(Column(
            SmallSplit("Copy", IconFont.Copy, "Copy the selected cells (Ctrl+C)", () => _form?.CopySelection(),
                ("Copy Whole Table", () => _form?.CopyTable())),
            Small("Paste", IconFont.Paste, "Paste into the table at the selection (Ctrl+V)", () => _form?.Paste(), _tableItems),
            SmallSplit("Revert", IconFont.Revert, "Revert the selected cells to their original values", () => _form?.RunEdit("Revert selected", t => t.UndoSelected()),
                ("Revert Whole Table", () => _form?.RunEdit("Revert table", t => t.UndoAll())))));
    }

    [MemberNotNull(nameof(_fineField), nameof(_coarseField), nameof(_valueField))]
    private void BuildAdjustGroup()
    {
        var g = AddGroup("Adjust");
        int height = Px(22), button = Px(20), icon = Px(12);

        // [ - | step | + ]: the step is editable; the buttons apply it to the selected cells.
        _fineField = new StepField(Px(34), height, button) { ToolTipText = "Fine step (editable)" };
        _fineField.AddButton(left: true, IconFont.Render(IconFont.Minus, icon, Red), "Decrease by the fine step  ( [  or  Ctrl+Shift+Down )", () => Run(GridCommand.DecrementFine));
        _fineField.AddButton(left: false, IconFont.Render(IconFont.Plus, icon, Green), "Increase by the fine step  ( ]  or  Ctrl+Shift+Up )", () => Run(GridCommand.IncrementFine));

        _coarseField = new StepField(Px(34), height, button) { ToolTipText = "Coarse step (editable)" };
        _coarseField.AddButton(left: true, IconFont.Render(IconFont.Minus, icon, Red), "Decrease by the coarse step  ( _  or  Ctrl+Down )", () => Run(GridCommand.DecrementCoarse));
        _coarseField.AddButton(left: false, IconFont.Render(IconFont.Plus, icon, Green), "Increase by the coarse step  ( +  or  Ctrl+Up )", () => Run(GridCommand.IncrementCoarse));

        // [ value | set | multiply ]
        _valueField = new StepField(Px(56), height, button)
        {
            ToolTipText = "Value to set, or factor to multiply by.\nType a number in a table to start; Enter sets, Ctrl+Enter multiplies.",
        };
        _valueField.AddButton(left: false, IconFont.Render(IconFont.Accept, icon, Accent), "Set the selected cells to the value (Enter)", SetValue);
        _valueField.AddButton(left: false, IconFont.Render(IconFont.Multiply, icon, Accent), "Multiply the selected cells by the value  ( *  or  Ctrl+Enter )", () => Run(GridCommand.Multiply));

        // Steps sit centred between their buttons.
        _fine.TextAlign = _coarse.TextAlign = HorizontalAlignment.Center;

        g.Controls.Add(Rows(
            FieldRow("Fine", _fineField),
            FieldRow("Coarse", _coarseField),
            FieldRow("Value", _valueField)));
    }

    /// <summary>A gray label followed by a field; the field is enabled with the table commands.</summary>
    private ToolStrip FieldRow(string label, StepField field)
    {
        var row = Strip(large: false);
        row.Padding = new Padding(0, 1, 0, 1);
        row.Items.Add(RowLabel(label));
        var host = new ToolStripControlHost(field) { AutoSize = false, Size = field.Size, Margin = new Padding(0, 1, 0, 1) };
        row.Items.Add(host);
        _tableItems.Add(host);
        return row;
    }

    private void BuildInterpolateGroup()
    {
        var g = AddGroup("Interpolate");
        g.Controls.Add(Column(
            Small("Both", IconFont.Interpolate, "Interpolate the selection in both directions (Shift+I)", () => Run(GridCommand.Interpolate), _gridItems, Purple),
            Small("Vertical", IconFont.Vertical, "Interpolate down the columns of the selection (Shift+V)", () => Run(GridCommand.VerticalInterpolate), _gridItems, Purple),
            Small("Horizontal", IconFont.Horizontal, "Interpolate along the rows of the selection (Shift+H)", () => Run(GridCommand.HorizontalInterpolate), _gridItems, Purple)));
    }

    [MemberNotNull(nameof(_rescale))]
    private void BuildAxisGroup()
    {
        var g = AddGroup("Axis");
        var big = Strip(large: true);
        _rescale = Large("Rescale", IconFont.Rescale, "Rescale an axis: new breakpoints, with the data resampled so lookups stay the same", () => _form?.RescaleAxis(), Purple);
        big.Items.Add(_rescale);
        g.Controls.Add(big);
    }

    [MemberNotNull(nameof(_xScaleLabel), nameof(_yScaleLabel), nameof(_showChanges), nameof(_graph))]
    private void BuildViewGroup()
    {
        var g = AddGroup("View");
        _scale.Width = _xScale.Width = _yScale.Width = Px(118);

        var valuesRow = Strip(large: false);
        valuesRow.Items.Add(RowLabel("Values"));
        valuesRow.Items.Add(_scale);
        _tableItems.Add(_scale);

        var xRow = Strip(large: false);
        _xScaleLabel = RowLabel("X axis");
        xRow.Items.Add(_xScaleLabel);
        xRow.Items.Add(_xScale);

        var yRow = Strip(large: false);
        _yScaleLabel = RowLabel("Y axis");
        yRow.Items.Add(_yScaleLabel);
        yRow.Items.Add(_yScale);
        g.Controls.Add(Rows(valuesRow, xRow, yRow));

        _showChanges = Small("Changes", IconFont.History, "Show differences from the original values instead of the values", () => _form?.ToggleShowChanges(), _gridItems);
        _graph = Small("Graph", IconFont.Chart, "Show or hide the graph of this table (Ctrl+G)", () => _form?.ToggleGraph(), null);
        g.Controls.Add(Column(
            _graph,
            _showChanges,
            Small("Select Data", IconFont.SelectAll, "Select every data cell (click the table's top-left corner, or Ctrl+A for everything)", () => _form?.Grid?.SelectAllData(), _gridItems)));
    }

    private void BuildToolsGroup()
    {
        var g = AddGroup("Tools");
        g.Controls.Add(Column(
            Small("Definitions", IconFont.Library, "Choose the definition files used to recognise images", () => _main.ShowDefinitionManager(), null, Accent),
            Small("Compare", IconFont.Compare, "Compare images: list the tables that differ between two open images", () => _main.ShowCompareImages(), null),
            Small("Settings", IconFont.Settings, "Editor settings", () => _main.ShowSettings(), null)));
    }

    // ----- building blocks -----

    private FlowLayoutPanel AddGroup(string caption)
    {
        var group = new RibbonGroup(caption, Line) { Margin = new Padding(0, 0, 2, 0) };
        _groups.Controls.Add(group);
        return group.Content;
    }

    private ToolStrip Strip(bool large, bool vertical = false) => new()
    {
        GripStyle = ToolStripGripStyle.Hidden,
        Renderer = _renderer,
        Dock = DockStyle.None,
        AutoSize = true,
        CanOverflow = false,
        LayoutStyle = vertical ? ToolStripLayoutStyle.VerticalStackWithOverflow : ToolStripLayoutStyle.HorizontalStackWithOverflow,
        BackColor = Back,
        ImageScalingSize = large ? new Size(Px(32), Px(32)) : new Size(Px(16), Px(16)),
        Padding = new Padding(0),
        Margin = new Padding(1, 0, 1, 0),
        ShowItemToolTips = true,
        TabStop = false,
    };

    private Control Column(params ToolStripItem[] items)
    {
        var strip = Strip(large: false, vertical: true);
        strip.Items.AddRange(items);
        return strip;
    }

    private static Control Rows(params ToolStrip[] rows)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Padding = new Padding(0), BackColor = Back };
        foreach (var row in rows)
            panel.Controls.Add(row);
        return panel;
    }

    private ToolStripButton Large(string text, char glyph, string tip, Action action, Color color, List<ToolStripItem>? list = null)
    {
        var button = new ToolStripButton(text)
        {
            Image = IconFont.Render(glyph, Px(32), color),
            ImageScaling = ToolStripItemImageScaling.None,
            TextImageRelation = TextImageRelation.ImageAboveText,
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ToolTipText = tip,
            AutoToolTip = false,
            Padding = new Padding(Px(4), Px(4), Px(4), Px(3)),
            Margin = new Padding(1, 1, 1, 0),
        };
        button.Click += (_, _) => action();
        list?.Add(button);
        return button;
    }

    private ToolStripButton Small(string text, char glyph, string tip, Action action, List<ToolStripItem>? list, Color? color = null)
    {
        var button = new ToolStripButton(text)
        {
            Image = IconFont.Render(glyph, Px(16), color ?? IconColor),
            ImageScaling = ToolStripItemImageScaling.None,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ImageAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleLeft,
            ToolTipText = tip,
            AutoToolTip = false,
            Padding = new Padding(Px(2), Px(2), Px(4), Px(2)),
        };
        button.Click += (_, _) => action();
        list?.Add(button);
        return button;
    }

    /// <summary>A small button with a drop-down of related commands.</summary>
    private ToolStripSplitButton SmallSplit(string text, char glyph, string tip, Action action, params (string Text, Action Action)[] more)
    {
        var button = new ToolStripSplitButton(text)
        {
            Image = IconFont.Render(glyph, Px(16), IconColor),
            ImageScaling = ToolStripItemImageScaling.None,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ImageAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleLeft,
            ToolTipText = tip,
            AutoToolTip = false,
            Padding = new Padding(Px(2), Px(2), Px(2), Px(2)),
        };
        button.ButtonClick += (_, _) => action();
        foreach (var (itemText, itemAction) in more)
            button.DropDownItems.Add(itemText, null, (_, _) => itemAction());
        _tableItems.Add(button);
        return button;
    }

    private ToolStripButton Icon(char glyph, string tip, Action action, Color color, string? text = null, List<ToolStripItem>? list = null)
    {
        var button = text == null
            ? new ToolStripButton { DisplayStyle = ToolStripItemDisplayStyle.Image }
            : new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, TextImageRelation = TextImageRelation.ImageBeforeText };
        button.Image = IconFont.Render(glyph, Px(16), color);
        button.ImageScaling = ToolStripItemImageScaling.None;
        button.ToolTipText = tip;
        button.AutoToolTip = false;
        button.Padding = new Padding(Px(2));
        button.Click += (_, _) => action();
        (list ?? _tableItems).Add(button);
        return button;
    }

    /// <summary>A plain gray caption: visibly different from the bordered controls next to it.</summary>
    private ToolStripLabel RowLabel(string text) => new(text)
    {
        AutoSize = false,
        Width = Px(44),
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = LabelColor,
    };

    // ----- state -----

    public double FineIncrement => ParseOr(_fine.Text, _form?.Table.CurrentScale.FineIncrement ?? 1);

    public double CoarseIncrement => ParseOr(_coarse.Text, _form?.Table.CurrentScale.CoarseIncrement ?? 1);

    public string ValueText => _value.Text;

    private static double ParseOr(string text, double fallback) => ValueFormatter.TryParse(text, out var v) ? v : fallback;

    /// <summary>Enables image commands and labels undo/redo for the current image.</summary>
    public void UpdateState(Rom? rom)
    {
        foreach (var item in _romItems)
            item.Enabled = rom != null;
        var history = rom?.History;
        _undo.Enabled = history?.CanUndo == true;
        _redo.Enabled = history?.CanRedo == true;
        _undo.ToolTipText = history?.UndoName is { } u ? $"Undo {u} (Ctrl+Z)" : "Undo (Ctrl+Z)";
        _redo.ToolTipText = history?.RedoName is { } r ? $"Redo {r} (Ctrl+Y)" : "Redo (Ctrl+Y)";
    }

    /// <summary>Shows the active table's steps and scales and enables the table commands.</summary>
    public void Bind(TableForm? form)
    {
        _form = form;
        _binding = true;
        try
        {
            bool editable = form != null && form.Table is not TableSwitch;
            foreach (var item in _tableItems)
                item.Enabled = editable;
            foreach (var item in _gridItems)
                item.Enabled = editable && form?.Grid != null;
            _showChanges.Checked = form?.ShowingChanges == true;
            _graph.Enabled = form?.GraphAvailable == true;
            _graph.Checked = form?.GraphVisible == true;
            _rescale.Enabled = form?.CanRescaleAxis == true;

            if (form == null || !editable)
            {
                _fine.Text = _coarse.Text = "";
                FillScales(_scale, null);
                FillScales(_xScale, null);
                FillScales(_yScale, null);
                return;
            }

            var table = form.Table;
            var scale = table.CurrentScale;
            _fine.Text = scale.FineIncrement.ToString(CultureInfo.CurrentCulture) ?? "";
            _coarse.Text = scale.CoarseIncrement.ToString(CultureInfo.CurrentCulture) ?? "";
            FillScales(_scale, table);
            _xScaleLabel.Text = table is Table3D ? "X axis" : "Axis";
            FillScales(_xScale, XAxis(form));
            FillScales(_yScale, (table as Table3D)?.YAxis);
        }
        finally
        {
            _binding = false;
        }
    }

    private static Table1D? XAxis(TableForm? form) => form?.Table switch
    {
        Table3D t3 => t3.XAxis,
        Table2D t2 => t2.Axis,
        _ => null,
    };

    /// <summary>Lists a table's (or axis's) scalings; disabled when it has none to choose from.</summary>
    private static void FillScales(ToolStripComboBox combo, Table? table)
    {
        combo.Items.Clear();
        if (table == null)
        {
            combo.Enabled = false;
            return;
        }
        if (table.IsStaticDataTable)
        {
            combo.Items.Add("(labels)");
            combo.SelectedIndex = 0;
            combo.Enabled = false;
            return;
        }
        foreach (var s in table.Scales)
            combo.Items.Add(TableForm.ScaleText(s));
        combo.SelectedIndex = table.CurrentScale == null ? -1 : table.Scales.IndexOf(table.CurrentScale);
        combo.Enabled = table.Scales.Count > 1;
    }

    private void ScaleSelected(ToolStripComboBox combo, Table? table)
    {
        if (_binding || _form == null || table == null || table.IsStaticDataTable || combo.SelectedIndex < 0 || combo.SelectedIndex >= table.Scales.Count)
            return;
        table.SetCurrentScale(table.Scales[combo.SelectedIndex]);
        Bind(_form);
        _form.Grid?.Focus();
    }

    /// <summary>Editing the steps changes the scale's increments, as in RomRaider.</summary>
    private void StoreIncrements()
    {
        var scale = _form?.Table.CurrentScale;
        if (scale == null)
            return;
        if (ValueFormatter.TryParse(_fine.Text, out var fine))
            scale.FineIncrement = fine;
        if (ValueFormatter.TryParse(_coarse.Text, out var coarse))
            scale.CoarseIncrement = coarse;
    }

    private void IncrementBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Escape)
        {
            StoreIncrements();
            e.SuppressKeyPress = true;
            _form?.Grid?.Focus();
        }
    }

    /// <summary>Starts typing a value: called when a number key is pressed in a table.</summary>
    public void BeginValueEntry(char first)
    {
        if (_form == null || !_value.Enabled)
            return;
        _value.Text = first.ToString();
        _value.Focus();
        _value.SelectionStart = _value.Text.Length;
        _value.SelectionLength = 0;
    }

    private void ValueKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            if (e.Control)
                Run(GridCommand.Multiply);
            else
                SetValue();
            _form?.Grid?.Focus();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            _form?.Grid?.Focus();
        }
    }

    private void SetValue() => _form?.SetValue(_value.Text);

    private void Run(GridCommand command) => _form?.RunCommand(command);

    // ----- visuals -----

    /// <summary>A ribbon group: its controls above a caption, with a divider on the right.</summary>
    private sealed class RibbonGroup : TableLayoutPanel
    {
        private readonly Color _line;

        public RibbonGroup(string caption, Color line)
        {
            _line = line;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Back;
            Padding = new Padding(4, 0, 8, 0);
            ColumnCount = 1;
            RowCount = 2;
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            DoubleBuffered = true;
            Content.BackColor = Back;
            var label = new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 17,
                Margin = new Padding(0),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(96, 100, 108),
                Font = UiFonts.Small,
            };
            Controls.Add(Content, 0, 0);
            Controls.Add(label, 0, 1);
        }

        public FlowLayoutPanel Content { get; } = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(_line);
            e.Graphics.DrawLine(pen, Width - 1, 4, Width - 1, Height - 5);
        }
    }

    private sealed class RibbonRenderer() : ToolStripProfessionalRenderer(new RibbonColors())
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Back);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }
    }

    private sealed class RibbonColors : ProfessionalColorTable
    {
        private static readonly Color Hover = Color.FromArgb(224, 238, 250);
        private static readonly Color HoverBorder = Color.FromArgb(180, 214, 242);
        private static readonly Color Pressed = Color.FromArgb(200, 225, 246);

        public RibbonColors()
        {
            UseSystemColors = false;
        }

        public override Color ButtonSelectedHighlight => Hover;
        public override Color ButtonSelectedGradientBegin => Hover;
        public override Color ButtonSelectedGradientMiddle => Hover;
        public override Color ButtonSelectedGradientEnd => Hover;
        public override Color ButtonSelectedBorder => HoverBorder;
        public override Color ButtonPressedGradientBegin => Pressed;
        public override Color ButtonPressedGradientMiddle => Pressed;
        public override Color ButtonPressedGradientEnd => Pressed;
        public override Color ButtonPressedBorder => HoverBorder;
        public override Color ButtonPressedHighlight => Pressed;
        public override Color ButtonCheckedGradientBegin => Pressed;
        public override Color ButtonCheckedGradientMiddle => Pressed;
        public override Color ButtonCheckedGradientEnd => Pressed;
        public override Color ButtonCheckedHighlight => Pressed;
        public override Color ButtonCheckedHighlightBorder => HoverBorder;
        public override Color CheckBackground => Pressed;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Pressed;
        public override Color ToolStripBorder => Back;
        public override Color ToolStripGradientBegin => Back;
        public override Color ToolStripGradientMiddle => Back;
        public override Color ToolStripGradientEnd => Back;
    }
}
