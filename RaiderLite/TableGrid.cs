using System.ComponentModel;
using RaiderLite.Core.Model;

namespace RaiderLite;

public enum CellRole
{
    Empty,
    Data,
    XAxis,
    YAxis,
}

public enum CompareDisplay
{
    Absolute,
    Percent,
}

/// <summary>Keyboard commands the grid forwards to its table window.</summary>
public enum GridCommand
{
    IncrementCoarse,
    DecrementCoarse,
    IncrementFine,
    DecrementFine,
    Multiply,
    Copy,
    Paste,
    Interpolate,
    VerticalInterpolate,
    HorizontalInterpolate,
    Undo,
    Redo,
}

public readonly record struct GridCell(CellRole Role, DataCell? Cell);

/// <summary>
/// Displays a table's cells: 1D as a row, 2D as axis row over data row, 3D as a grid
/// with the X axis on top and Y axis on the left. Values are not edited in place;
/// typing starts value entry in the table toolbar, as in RomRaider.
/// </summary>
public sealed class TableGrid : DataGridView
{
    private readonly Table _table;
    private GridCell[,] _map = new GridCell[0, 0];
    private bool _syncingSelection;
    private readonly Dictionary<Table, (double Min, double Max)> _ranges = [];

    public TableGrid(Table table)
    {
        _table = table;
        DoubleBuffered = true;
        VirtualMode = true;
        ReadOnly = true;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AllowUserToResizeColumns = true;
        AllowUserToOrderColumns = false;
        RowHeadersVisible = false;
        ColumnHeadersVisible = false;
        SelectionMode = DataGridViewSelectionMode.CellSelect;
        MultiSelect = true;
        BorderStyle = BorderStyle.None;
        BackgroundColor = SystemColors.Window;
        CellBorderStyle = DataGridViewCellBorderStyle.None;
        ShowCellToolTips = true;
        StandardTab = true;
        EditMode = DataGridViewEditMode.EditProgrammatically;
        DefaultCellStyle.Font = UiFonts.Regular;
        DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        BuildLayout();
    }

    public Table Table => _table;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CompareDisplay CompareDisplay { get; set; } = CompareDisplay.Absolute;

    /// <summary>Raised for RomRaider keyboard shortcuts.</summary>
    public event EventHandler<GridCommand>? Command;

    /// <summary>Raised when a value-entry key (digit, '.', '-') is typed.</summary>
    public event EventHandler<char>? ValueEntryStarted;

    public GridCell GetGridCell(int row, int col) =>
        row >= 0 && col >= 0 && row < _map.GetLength(0) && col < _map.GetLength(1) ? _map[row, col] : default;

    public GridCell CurrentGridCell => CurrentCell == null ? default : GetGridCell(CurrentCell.RowIndex, CurrentCell.ColumnIndex);

    private void BuildLayout()
    {
        switch (_table)
        {
            case Table3D t3:
            {
                int sx = t3.Data3D.GetLength(0), sy = t3.Data3D.GetLength(1);
                _map = new GridCell[sy + 1, sx + 1];
                for (int x = 0; x < sx; x++)
                    _map[0, x + 1] = new GridCell(CellRole.XAxis, t3.XAxis.Cells.ElementAtOrDefault(x));
                for (int y = 0; y < sy; y++)
                {
                    _map[y + 1, 0] = new GridCell(CellRole.YAxis, t3.YAxis.Cells.ElementAtOrDefault(y));
                    for (int x = 0; x < sx; x++)
                        _map[y + 1, x + 1] = new GridCell(CellRole.Data, t3.Data3D[x, y]);
                }
                break;
            }
            case Table2D t2:
            {
                int n = t2.Cells.Length;
                _map = new GridCell[2, n];
                for (int i = 0; i < n; i++)
                {
                    _map[0, i] = new GridCell(CellRole.XAxis, t2.Axis.Cells.ElementAtOrDefault(i));
                    _map[1, i] = new GridCell(CellRole.Data, t2.Cells[i]);
                }
                break;
            }
            default:
            {
                int n = _table.Cells.Length;
                _map = new GridCell[1, n];
                for (int i = 0; i < n; i++)
                    _map[0, i] = new GridCell(CellRole.Data, _table.Cells[i]);
                break;
            }
        }

        Columns.Clear();
        int cols = _map.GetLength(1);
        for (int c = 0; c < cols; c++)
        {
            Columns.Add(new DataGridViewTextBoxColumn
            {
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Resizable = DataGridViewTriState.True,
            });
        }
        RowCount = _map.GetLength(0);
        RowTemplate.Height = AppSettings.Current.CellHeight;
        foreach (DataGridViewRow row in Rows)
            row.Height = AppSettings.Current.CellHeight;

        // Keep the axes in view when the grid scrolls.
        if (_table is Table3D or Table2D && RowCount > 1)
            Rows[0].Frozen = true;
        if (_table is Table3D && Columns.Count > 1)
            Columns[0].Frozen = true;

        RefreshValues();
        AutoSizeCells();
    }

    /// <summary>Sizes columns to fit their widest value.</summary>
    public void AutoSizeCells()
    {
        var font = DefaultCellStyle.Font ?? Font;
        var bold = UiFonts.Bold;
        for (int c = 0; c < _map.GetLength(1); c++)
        {
            int width = AppSettings.Current.CellWidth;
            for (int r = 0; r < _map.GetLength(0); r++)
            {
                var gc = _map[r, c];
                if (gc.Cell == null)
                    continue;
                var size = TextRenderer.MeasureText(GetText(gc), gc.Role == CellRole.Data ? font : bold);
                width = Math.Max(width, size.Width + 10);
            }
            Columns[c].Width = width;
        }
    }

    /// <summary>Preferred size to show every cell without scrolling.</summary>
    public Size ContentSize
    {
        get
        {
            int w = Columns.Cast<DataGridViewColumn>().Sum(c => c.Width);
            int h = Rows.Cast<DataGridViewRow>().Sum(r => r.Height);
            return new Size(w + 2, h + 2);
        }
    }

    /// <summary>Recomputes colour ranges and repaints. Call after values change.</summary>
    public void RefreshValues()
    {
        _ranges.Clear();
        ComputeRange(_table, _table.DataCells);
        foreach (var axis in _table.Axes)
            ComputeRange(axis, axis.DataCells);
        Invalidate();
    }

    private void ComputeRange(Table t, IEnumerable<DataCell> cells)
    {
        bool compare = _table.CompareTable != null;
        double min = double.MaxValue, max = double.MinValue;
        foreach (var cell in cells)
        {
            if (cell.IsStatic)
                continue;
            double v = compare ? cell.CompareValue : cell.RealValue;
            if (double.IsNaN(v))
                continue;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }
        if (min <= max)
            _ranges[t] = (min, max);
    }

    public string GetText(GridCell gc)
    {
        var cell = gc.Cell;
        if (cell == null)
            return "";
        if (cell.IsStatic)
            return cell.StaticText ?? "";
        var scale = cell.Table.CurrentScale;
        if (_table.CompareTable != null)
        {
            if (CompareDisplay == CompareDisplay.Percent)
                return ValueFormatter.Format(cell.RealCompareChangeValue * 100, "0.0") + "%";
            return ValueFormatter.Format(cell.RealCompareValue, scale.Format);
        }
        return ValueFormatter.Format(cell.RealValue, scale.Format);
    }

    protected override void OnCellValueNeeded(DataGridViewCellValueEventArgs e)
    {
        e.Value = GetText(GetGridCell(e.RowIndex, e.ColumnIndex));
        base.OnCellValueNeeded(e);
    }

    protected override void OnCellToolTipTextNeeded(DataGridViewCellToolTipTextNeededEventArgs e)
    {
        var gc = GetGridCell(e.RowIndex, e.ColumnIndex);
        if (gc.Cell is { IsStatic: false } cell)
        {
            var scale = cell.Table.CurrentScale;
            var tip = $"{ValueFormatter.Format(cell.RealValue, scale.Format)} {scale.Unit}\n" +
                      $"Raw: {cell.BinValue} (0x{(long)cell.BinValue & 0xFFFFFFFF:X})\n" +
                      $"Address: 0x{cell.Address:X}";
            if (cell.IsChanged)
                tip += $"\nOriginal: {ValueFormatter.Format(scale.ToReal(cell.OriginalValue), scale.Format)}";
            if (_table.CompareTable != null)
                tip += $"\nCompared: {ValueFormatter.Format(scale.ToReal(cell.CompareToValue), scale.Format)}";
            e.ToolTipText = tip;
        }
        base.OnCellToolTipTextNeeded(e);
    }

    private Color BackgroundFor(GridCell gc)
    {
        var settings = AppSettings.Current;
        var cell = gc.Cell;
        if (cell == null)
            return SystemColors.Control;
        bool isAxis = gc.Role != CellRole.Data;
        if (cell.IsStatic || (isAxis && !settings.ColorAxis))
            return settings.AxisColor;
        if (settings.ValueLimitWarning && cell.IsOutOfRange)
            return settings.WarningColor;

        if (!_ranges.TryGetValue(cell.Table, out var range))
            return settings.AxisColor;

        if (_table.CompareTable != null)
        {
            if (cell.CompareValue == 0)
                return Color.FromArgb(160, 160, 160);
            if (range.Min == range.Max)
                return settings.MaxColor;
            return ColorScaler.GetScaledColor((cell.CompareValue - range.Min) / (range.Max - range.Min), settings.MinColor, settings.MaxColor);
        }

        double scale = range.Max == range.Min ? 0.5 : (cell.RealValue - range.Min) / (range.Max - range.Min);
        return ColorScaler.GetScaledColor(scale, settings.MinColor, settings.MaxColor);
    }

    private bool _paintFailed;

    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        base.OnCellPainting(e);
        if (_paintFailed || e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics == null)
            return;
        try
        {
            PaintCell(e, e.Graphics);
        }
        catch (Exception ex)
        {
            // Reporting from inside painting would repeat on every repaint: log once and
            // fall back to the standard cell painting for the rest of this window's life.
            ErrorReporter.Log(ex);
            _paintFailed = true;
            e.Handled = false;
            Invalidate();
        }
    }

    private void PaintCell(DataGridViewCellPaintingEventArgs e, Graphics graphics)
    {
        var settings = AppSettings.Current;
        var gc = GetGridCell(e.RowIndex, e.ColumnIndex);
        var bounds = e.CellBounds;
        bool selected = (e.State & DataGridViewElementStates.Selected) != 0 && gc.Cell is { IsStatic: false };

        var back = selected ? settings.HighlightColor : BackgroundFor(gc);
        using (var brush = new SolidBrush(back))
            graphics.FillRectangle(brush, bounds);

        // Grid lines.
        using (var pen = new Pen(gc.Cell == null ? SystemColors.Control : Color.FromArgb(90, 90, 90)))
            graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

        // Changed cells: a border in the increased colour (red by default) or the decreased colour
        // (blue), against the original value, or the compared value when comparing.
        if (gc.Cell is { IsStatic: false } cell)
        {
            double reference = _table.CompareTable == null ? cell.OriginalValue : cell.CompareToValue;
            Color? border = cell.BinValue > reference ? settings.IncreaseBorder
                : cell.BinValue < reference ? settings.DecreaseBorder
                : null;
            if (border is { } b)
            {
                using var pen = new Pen(b, 2);
                graphics.DrawRectangle(pen, bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3);
            }
        }

        if (gc.Cell != null)
        {
            var font = gc.Role == CellRole.Data && gc.Cell is { IsStatic: false } ? e.CellStyle?.Font ?? UiFonts.Regular : UiFonts.Bold;
            var fore = selected ? Color.White : ColorScaler.ContrastText(back);
            TextRenderer.DrawText(graphics, GetText(gc), font, bounds, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // Focus rectangle on the current cell.
        if (Focused && CurrentCell != null && CurrentCell.RowIndex == e.RowIndex && CurrentCell.ColumnIndex == e.ColumnIndex)
        {
            using var pen = new Pen(Color.Black) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            graphics.DrawRectangle(pen, bounds.X + 2, bounds.Y + 2, bounds.Width - 5, bounds.Height - 5);
        }

        e.Handled = true;
    }

    protected override void OnSelectionChanged(EventArgs e)
    {
        if (!_syncingSelection)
            SyncSelectionToModel();
        base.OnSelectionChanged(e);
    }

    private void SyncSelectionToModel()
    {
        foreach (var cell in _table.AllCells)
            cell.IsSelected = false;
        foreach (DataGridViewCell gridCell in SelectedCells)
        {
            var gc = GetGridCell(gridCell.RowIndex, gridCell.ColumnIndex);
            if (gc.Cell != null)
                gc.Cell.IsSelected = true;
        }
    }

    /// <summary>Selects every data cell (not axes).</summary>
    public void SelectAllData()
    {
        _syncingSelection = true;
        try
        {
            ClearSelection();
            for (int r = 0; r < _map.GetLength(0); r++)
            {
                for (int c = 0; c < _map.GetLength(1); c++)
                {
                    if (_map[r, c].Role == CellRole.Data)
                        Rows[r].Cells[c].Selected = true;
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }
        SyncSelectionToModel();
    }

    protected override void OnCellMouseDown(DataGridViewCellMouseEventArgs e)
    {
        // Clicking the empty corner of a 3D table selects all data.
        if (e.Button == MouseButtons.Left && GetGridCell(e.RowIndex, e.ColumnIndex).Role == CellRole.Empty && _table is Table3D)
        {
            SelectAllData();
            return;
        }
        base.OnCellMouseDown(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        GridCommand? command = keyData switch
        {
            Keys.Control | Keys.Up => GridCommand.IncrementCoarse,
            Keys.Control | Keys.Down => GridCommand.DecrementCoarse,
            Keys.Control | Keys.Shift | Keys.Up => GridCommand.IncrementFine,
            Keys.Control | Keys.Shift | Keys.Down => GridCommand.DecrementFine,
            Keys.Control | Keys.Oemplus or Keys.Control | Keys.Add => GridCommand.IncrementFine,
            Keys.Control | Keys.OemMinus or Keys.Control | Keys.Subtract => GridCommand.DecrementFine,
            Keys.Control | Keys.Enter => GridCommand.Multiply,
            Keys.Control | Keys.C => GridCommand.Copy,
            Keys.Control | Keys.Insert => GridCommand.Copy,
            Keys.Control | Keys.V => GridCommand.Paste,
            Keys.Shift | Keys.Insert => GridCommand.Paste,
            Keys.Shift | Keys.I => GridCommand.Interpolate,
            Keys.Shift | Keys.V => GridCommand.VerticalInterpolate,
            Keys.Shift | Keys.H => GridCommand.HorizontalInterpolate,
            Keys.Control | Keys.Z => GridCommand.Undo,
            Keys.Control | Keys.Y => GridCommand.Redo,
            Keys.Control | Keys.Shift | Keys.Z => GridCommand.Redo,
            _ => null,
        };
        if (command is { } c)
        {
            Command?.Invoke(this, c);
            return true;
        }
        if (keyData == (Keys.Control | Keys.A))
        {
            SelectAll();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        char ch = e.KeyChar;
        GridCommand? command = ch switch
        {
            '+' => GridCommand.IncrementCoarse,
            '_' => GridCommand.DecrementCoarse,
            ']' => GridCommand.IncrementFine,
            '[' => GridCommand.DecrementFine,
            '*' => GridCommand.Multiply,
            _ => null,
        };
        if (command is { } c)
        {
            Command?.Invoke(this, c);
            e.Handled = true;
            return;
        }
        if (char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-')
        {
            ValueEntryStarted?.Invoke(this, ch);
            e.Handled = true;
            return;
        }
        base.OnKeyPress(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    /// <summary>The cell's position in the grid, for selecting from code.</summary>
    public Point? FindCell(DataCell cell)
    {
        for (int r = 0; r < _map.GetLength(0); r++)
        {
            for (int c = 0; c < _map.GetLength(1); c++)
            {
                if (ReferenceEquals(_map[r, c].Cell, cell))
                    return new Point(c, r);
            }
        }
        return null;
    }
}
