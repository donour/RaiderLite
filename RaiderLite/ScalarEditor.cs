using System.Globalization;
using RaiderLite.Core.Model;

namespace RaiderLite;

/// <summary>
/// Editor for a table holding one value: a single row, <c>[ − | value | + ]  units ▾</c>.
/// The value is typed in place; raw value and address show in the tooltip and status bar.
/// </summary>
public sealed class ScalarEditor : UserControl
{
    private const int Pad = 6;
    private static readonly Color Increased = Color.FromArgb(255, 232, 232);
    private static readonly Color Decreased = Color.FromArgb(230, 238, 255);
    private static readonly Color OutOfRange = Color.FromArgb(255, 200, 200);

    private readonly TableForm _form;
    private readonly Table _table;
    private readonly DataCell _cell;
    private readonly StepField _field;
    private readonly Label _units = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand, ForeColor = Color.FromArgb(70, 74, 82) };
    private readonly ToolTip _tips = new() { AutoPopDelay = 30000 };
    private string _shownText = "";

    private TextBox _value => _field.Box;

    public ScalarEditor(TableForm form, Table table)
    {
        _form = form;
        _table = table;
        _cell = table.DataCells.First();
        BackColor = SystemColors.Window;

        float dpi = form.DeviceDpi / 96f;
        int Px(float v) => (int)Math.Round(v * dpi);
        _field = new StepField(Px(84), Px(26), Px(24));
        _field.Box.Font = UiFonts.Value;
        _field.AddButton(left: true, IconFont.Render(IconFont.Minus, Px(12), Color.FromArgb(196, 43, 28)),
            "Decrease by the fine step (Shift+click: coarse)", () => Step(-1, (ModifierKeys & Keys.Shift) != 0));
        _field.AddButton(left: false, IconFont.Render(IconFont.Plus, Px(12), Color.FromArgb(16, 124, 16)),
            "Increase by the fine step (Shift+click: coarse)", () => Step(1, (ModifierKeys & Keys.Shift) != 0));

        // Size the units label for the longest unit so the window doesn't jump when switching.
        var unitTexts = table.Scales.Select(UnitText).DefaultIfEmpty("raw \u25be");
        _units.Size = new Size(unitTexts.Max(t => TextRenderer.MeasureText(t, Font).Width) + Px(6), _field.Height);
        _units.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                TableForm.ShowScaleMenu(_table, _units, new Point(0, _units.Height));
        };
        _tips.SetToolTip(_units, "Click to change the scaling");

        Controls.Add(_field);
        Controls.Add(_units);
        _field.Location = new Point(Pad, Pad);
        _units.Location = new Point(_field.Right + Px(6), Pad);

        _value.KeyDown += ValueKeyDown;
        _value.Leave += (_, _) => Commit();

        // The toolbar's increment/set buttons act on selected cells.
        _cell.IsSelected = true;
        RefreshValue();
    }

    private static string UnitText(Scale s) => (s.IsRaw ? "raw" : s.Unit) + " \u25be";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _tips.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Client size that fits the row.</summary>
    public Size ContentSize => new(_units.Right + Pad, _field.Bottom + Pad);

    /// <summary>Whether a typed value is waiting to be committed.</summary>
    public bool IsEditing => _value.Modified && _value.Text.Trim() != _shownText;

    public void FocusValue()
    {
        _value.Focus();
        _value.SelectAll();
    }

    /// <summary>Shows the current value; call after the cell or scale changed.</summary>
    public void RefreshValue()
    {
        var scale = _table.CurrentScale;
        _shownText = ValueFormatter.Format(_cell.RealValue, scale.Format);
        if (!_value.Focused || _value.Text == _shownText || !_value.Modified)
        {
            _value.Text = _shownText;
            _value.Modified = false;
        }
        _units.Text = UnitText(scale);

        bool editable = _table.ReadOnlyReason == null;
        _value.ReadOnly = !editable;
        _field.FieldColor = _cell.IsOutOfRange && AppSettings.Current.ValueLimitWarning ? OutOfRange
            : _cell.BinValue > _cell.OriginalValue ? Increased
            : _cell.BinValue < _cell.OriginalValue ? Decreased
            : Color.White;

        long raw = (long)_cell.BinValue;
        var tip = $"Raw {_cell.BinValue.ToString(CultureInfo.CurrentCulture)} (0x{raw & 0xFFFFFFFF:X}) at 0x{_cell.Address:X}, {StorageType.Describe(_table.StorageType, _table.Signed)}";
        if (_cell.IsChanged)
            tip += $"\nChanged from {ValueFormatter.Format(scale.ToReal(_cell.OriginalValue), scale.Format)} (right-click to revert)";
        if (_table.Locked)
            tip += "\nLocked";
        else if (!editable)
            tip += $"\nNeeds user level {_table.UserLevel}";
        tip += "\n\nEnter sets, Esc cancels. Up/Down or wheel: fine step; Page Up/Down: coarse step.";
        if (TableForm.DescriptionTip(_table) is { } description)
            tip += "\n\n" + description;
        _field.ToolTipText = tip;
    }

    private void Commit()
    {
        var text = _value.Text.Trim();
        if (!_value.Modified || text == _shownText)
            return;
        _value.Modified = false;
        if (!ValueFormatter.TryParse(text, out _))
        {
            System.Media.SystemSounds.Beep.Play();
            RefreshValue();
            return;
        }
        _form.RunEdit("Set value", _ => _cell.SetRealValue(text));
        RefreshValue();
        _value.SelectAll();
    }

    private void Step(int direction, bool coarse)
    {
        var scale = _table.CurrentScale;
        if (_value.ReadOnly)
            return;
        Commit();
        double step = coarse ? scale.CoarseIncrement : scale.FineIncrement;
        _form.RunEdit(direction > 0 ? "Increment" : "Decrement", _ => _cell.Increment(direction * step));
        RefreshValue();
        _value.SelectAll();
    }

    private void ValueKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                Commit();
                e.SuppressKeyPress = true;
                break;
            case Keys.Escape:
                _value.Modified = false;
                RefreshValue();
                _value.SelectAll();
                e.SuppressKeyPress = true;
                break;
            case Keys.Up:
            case Keys.Down:
                Step(e.KeyCode == Keys.Up ? 1 : -1, coarse: e.Shift);
                e.SuppressKeyPress = true;
                break;
            case Keys.PageUp:
            case Keys.PageDown:
                Step(e.KeyCode == Keys.PageUp ? 1 : -1, coarse: true);
                e.SuppressKeyPress = true;
                break;
            case Keys.Z when e.Control && !_value.Modified:
                _form.RunCommand(GridCommand.Undo);
                e.SuppressKeyPress = true;
                break;
            case Keys.Y when e.Control:
                _form.RunCommand(GridCommand.Redo);
                e.SuppressKeyPress = true;
                break;
        }
    }
}
