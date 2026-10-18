using System.Globalization;
using RaiderLite.Core.Model;

namespace RaiderLite;

/// <summary>
/// Rescale one axis of a curve or map: pick the new breakpoints, preview them with the change to
/// the table's lookups, and on OK hand back the plan for the table window to apply.
/// </summary>
public sealed class RescaleAxisForm : DialogBase
{
    private enum Method
    {
        Stretch,
        Even,
        Manual,
    }

    private static readonly string[] MethodNames =
    [
        "Stretch: keep the first breakpoint and the spacing",
        "Even spacing between first and last",
        "Enter every breakpoint",
    ];

    private static readonly string[] BeyondNames =
    [
        "Hold the edge values (the ECU behaves as now)",
        "Extrapolate the end slopes",
    ];

    private readonly Table _table;
    private readonly List<Table1D> _axes;
    private readonly ComboBox _axis = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly Label _current = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    private readonly ComboBox _method = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly TextBox _first = new() { Width = 110 };
    private readonly TextBox _last = new() { Width = 110 };
    private readonly Label _units = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3), ForeColor = SystemColors.GrayText };
    private readonly TextBox _values = new() { Width = 360 };
    private readonly ComboBox _beyond = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly ListView _preview = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        Size = new Size(360, 190),
        MultiSelect = false,
    };
    private readonly Label _summary = new() { AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(3, 8, 3, 3) };
    private readonly Label _warnings = new() { AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = Color.FromArgb(157, 93, 0), Margin = new Padding(3, 4, 3, 3) };
    private readonly Label _error = new() { AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = Color.FromArgb(196, 43, 28), Margin = new Padding(3, 4, 3, 3) };
    private bool _filling;

    public RescaleAxisForm(Table table, Table1D? initialAxis) : base("Rescale Axis")
    {
        _table = table;
        _axes = table.Axes.Where(a => !a.IsStaticDataTable).ToList();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        OkButton.Text = "Rescale";

        foreach (var axis in _axes)
            _axis.Items.Add(AxisText(axis));
        _axis.Enabled = _axes.Count > 1;
        _method.Items.AddRange(MethodNames);
        _beyond.Items.AddRange(BeyondNames);
        _preview.Columns.Add("#", 40, HorizontalAlignment.Right);
        _preview.Columns.Add("Current", 140, HorizontalAlignment.Right);
        _preview.Columns.Add("New", 140, HorizontalAlignment.Right);

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

        var ends = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        ends.Controls.Add(new Label { Text = "First", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        ends.Controls.Add(_first);
        ends.Controls.Add(new Label { Text = "Last", AutoSize = true, Margin = new Padding(12, 6, 3, 3) });
        ends.Controls.Add(_last);
        ends.Controls.Add(_units);

        Row("Axis", _axis);
        Row("Now", _current);
        Row("Method", _method);
        Row("New range", ends);
        Row("Breakpoints", _values);
        Row("Beyond the old range", _beyond);
        Row("Preview", _preview);
        Full(_summary);
        Full(_warnings);
        Full(_error);
        Controls.Add(layout);
        layout.BringToFront();

        var tips = new ToolTip { AutoPopDelay = 30000 };
        Disposed += (_, _) => tips.Dispose();
        tips.SetToolTip(_values, "The new breakpoints in the axis's units, separated by spaces (pasting a copied row works).");
        tips.SetToolTip(_beyond, "Where the new axis reaches past the old one. ECU lookups clamp at the last breakpoint, " +
                                 "so holding the edge values keeps the engine's behaviour unchanged until you tune the new region.");

        _axis.SelectedIndexChanged += (_, _) => AxisChanged();
        _method.SelectedIndexChanged += (_, _) => MethodChanged();
        foreach (var box in new[] { _first, _last, _values })
            box.TextChanged += (_, _) => Recalculate();
        _beyond.SelectedIndexChanged += (_, _) => Recalculate();

        _filling = true;
        _method.SelectedIndex = 0;
        _beyond.SelectedIndex = 0;
        _filling = false;
        int initial = initialAxis != null ? _axes.IndexOf(initialAxis) : -1;
        _axis.SelectedIndex = _axes.Count == 0 ? -1 : Math.Max(0, initial);
        if (_axes.Count == 0)
            ShowError("This table has no axis stored in the image.");
    }

    /// <summary>The rescale to apply when the dialog closes with OK.</summary>
    public AxisRescalePlan? Plan { get; private set; }

    private Table1D? SelectedAxis => _axis.SelectedIndex >= 0 ? _axes[_axis.SelectedIndex] : null;

    private Method SelectedMethod => (Method)Math.Max(0, _method.SelectedIndex);

    private string AxisText(Table1D axis)
    {
        string kind = _table is Table3D ? (axis.AxisKind == AxisKind.X ? "X axis" : "Y axis") : "Axis";
        string units = axis.CurrentScale.IsRaw ? "raw" : axis.CurrentScale.Unit;
        return string.IsNullOrEmpty(axis.Name) ? $"{kind} ({units})" : $"{kind}: {axis.Name} ({units})";
    }

    private static string Show(double real, Scale scale) => ValueFormatter.Format(real, scale.Format);

    /// <summary>A value for an input box: enough digits that leaving it unchanged changes nothing.</summary>
    private static string Editable(double real) => real.ToString("0.######", CultureInfo.CurrentCulture);

    private void AxisChanged()
    {
        if (SelectedAxis is not { } axis)
            return;
        var scale = axis.CurrentScale;
        var reals = axis.Cells.Select(c => c.RealValue).ToArray();
        string units = scale.IsRaw ? "" : " " + scale.Unit;
        _current.Text = $"{Show(reals[0], scale)} to {Show(reals[^1], scale)}{units}, {reals.Length} breakpoints";
        _units.Text = scale.IsRaw ? "raw" : scale.Unit;
        _filling = true;
        _first.Text = Editable(reals[0]);
        _last.Text = Editable(reals[^1]);
        _values.Text = string.Join(" ", reals.Select(Editable));
        _filling = false;
        MethodChanged();
    }

    private void MethodChanged()
    {
        var method = SelectedMethod;
        _first.Enabled = method == Method.Even;
        _last.Enabled = method != Method.Manual;
        _values.Enabled = method == Method.Manual;
        if (SelectedAxis is { } axis && method != Method.Even)
            _first.Text = Editable(axis.Cells[0].RealValue);
        Recalculate();
    }

    /// <summary>The new breakpoints in real units from the inputs, or an error message.</summary>
    private (double[]? Values, string? Error) NewBreakpoints(Table1D axis)
    {
        var current = axis.Cells.Select(c => c.RealValue).ToArray();
        switch (SelectedMethod)
        {
            case Method.Stretch:
                if (!ValueFormatter.TryParse(_last.Text, out double stretchLast))
                    return (null, "Enter the new last breakpoint.");
                return (AxisRescaler.Stretch(current, stretchLast), null);
            case Method.Even:
                if (!ValueFormatter.TryParse(_first.Text, out double first))
                    return (null, "Enter the new first breakpoint.");
                if (!ValueFormatter.TryParse(_last.Text, out double last))
                    return (null, "Enter the new last breakpoint.");
                return (AxisRescaler.Even(first, last, current.Length), null);
            default:
                var tokens = _values.Text.Split([' ', '\t', '\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries);
                var values = new double[tokens.Length];
                for (int i = 0; i < tokens.Length; i++)
                {
                    if (!ValueFormatter.TryParse(tokens[i], out values[i]))
                        return (null, $"\"{tokens[i]}\" is not a number.");
                }
                return (values, null);
        }
    }

    private void Recalculate()
    {
        if (_filling || SelectedAxis is not { } axis)
            return;
        Plan = null;
        var (values, inputError) = NewBreakpoints(axis);
        var plan = values == null ? null : AxisRescaler.Plan(_table, axis, values, (BeyondRange)_beyond.SelectedIndex);
        Plan = plan;
        FillPreview(axis, plan);

        string? error = inputError ?? plan?.Error;
        if (error != null)
        {
            ShowError(error);
            return;
        }
        _error.Text = "";
        _error.Visible = false;
        _summary.Text = Summary(axis, plan!);
        _warnings.Text = string.Join(Environment.NewLine, plan!.Warnings.Select(w => "⚠ " + w));
        _warnings.Visible = plan.Warnings.Count > 0;
        OkButton.Enabled = true;
    }

    private void ShowError(string error)
    {
        _summary.Text = "";
        _warnings.Text = "";
        _warnings.Visible = false;
        _error.Text = error;
        _error.Visible = true;
        OkButton.Enabled = false;
    }

    private void FillPreview(Table1D axis, AxisRescalePlan? plan)
    {
        var scale = axis.CurrentScale;
        _preview.BeginUpdate();
        _preview.Items.Clear();
        for (int i = 0; i < axis.Cells.Length; i++)
        {
            var item = new ListViewItem((i + 1).ToString(CultureInfo.CurrentCulture));
            item.SubItems.Add(Show(axis.Cells[i].RealValue, scale));
            item.SubItems.Add(plan is { CanApply: true } ? Show(scale.ToReal(plan.NewAxis[i]), scale) : "");
            _preview.Items.Add(item);
        }
        _preview.EndUpdate();
    }

    private string Summary(Table1D axis, AxisRescalePlan plan)
    {
        var dataScale = _table.CurrentScale;
        var axisScale = axis.CurrentScale;
        string dataUnits = dataScale.IsRaw ? "" : " " + dataScale.Unit;
        string axisUnits = axisScale.IsRaw ? "" : " " + axisScale.Unit;
        var lines = new List<string>();

        if (plan.MaxChange == 0)
        {
            lines.Add("Lookups within the current axis range are unchanged.");
        }
        else
        {
            string percent = plan.DataRange > 0 ? $" ({Math.Abs(plan.MaxChange) / plan.DataRange:P1} of the table's range)" : "";
            lines.Add($"Largest change of a lookup within the current axis range: {Show(Math.Abs(plan.MaxChange), dataScale)}{dataUnits}{percent}, " +
                      $"at {Show(plan.MaxChangeAt, axisScale)}{axisUnits}. Between breakpoints the new table can't follow every bend of the old one.");
        }

        double oldLo = Math.Min(plan.OldAxis[0], plan.OldAxis[^1]), oldHi = Math.Max(plan.OldAxis[0], plan.OldAxis[^1]);
        double newLo = Math.Min(plan.NewAxis[0], plan.NewAxis[^1]), newHi = Math.Max(plan.NewAxis[0], plan.NewAxis[^1]);
        if (newLo < oldLo || newHi > oldHi)
        {
            lines.Add(plan.Beyond == BeyondRange.Hold
                ? "Beyond the old range the table holds its edge values, so the ECU behaves as it does now until you tune that region."
                : "Beyond the old range the values continue the end slopes: the ECU's behaviour changes there.");
        }
        lines.Add("Undo reverses the whole rescale in one step.");
        return string.Join(Environment.NewLine, lines);
    }

    protected override void OnOk(EventArgs e)
    {
        if (Plan is not { CanApply: true })
            DialogResult = DialogResult.None;
    }
}
