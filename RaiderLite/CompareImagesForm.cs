using RaiderLite.Core.Model;

namespace RaiderLite;

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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F1)
        {
            HelpForm.ShowTopic("comparing#compare-images");
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
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
        foreach (var c in ImageDiff.CompareRoms(a, b))
        {
            var (result, color) = c.Result switch
            {
                TableComparisonResult.Same => ("Same", EqualColor),
                TableComparisonResult.Different => ("Different", DifferentColor),
                TableComparisonResult.DifferentSize => ("Different size", DifferentColor),
                TableComparisonResult.MissingInSecond => ($"Missing in {b.FileName}", MissingColor),
                _ => ($"Missing in {a.FileName}", MissingColor),
            };
            switch (c.Result)
            {
                case TableComparisonResult.Same:
                    same++;
                    break;
                case TableComparisonResult.Different or TableComparisonResult.DifferentSize:
                    different++;
                    break;
                default:
                    missing++;
                    break;
            }
            if (c.Result == TableComparisonResult.Same && _differencesOnly.Checked)
                continue;
            _list.Items.Add(new ListViewItem([c.Name, c.Category, result]) { ForeColor = color, Tag = (c.First, c.Second) });
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
