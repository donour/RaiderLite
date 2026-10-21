using RaiderLite.Core.Tunes;

namespace RaiderLite;

/// <summary>New Revision: what it is based on, its label and an optional first note.</summary>
public sealed class NewRevisionForm : DialogBase
{
    private readonly ComboBox _basedOn = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    private readonly TextBox _label = new() { Width = 380 };
    private readonly TextBox _note = new() { Width = 380, Height = 90, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };

    public NewRevisionForm(Car car, Revision basedOn) : base("New Revision")
    {
        HelpTopic = "tunes#new-revisions";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        OkButton.Text = "Create";

        foreach (var r in car.Revisions.OrderByDescending(r => r.Number))
            _basedOn.Items.Add(r);
        _basedOn.SelectedItem = basedOn;
        _label.Text = basedOn.Number == 0 ? "" : basedOn.Label;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
        void Row(string text, Control control)
        {
            layout.Controls.Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(3, 6, 12, 3) });
            layout.Controls.Add(control);
        }
        Row("Based on", _basedOn);
        Row("Label", _label);
        Row("Note", _note);
        layout.Controls.Add(new Label
        {
            Text = $"Copies the image into a new folder of {car.Name} and opens it.\nThe revision it is based on keeps its image as it is.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 8, 3, 3),
        });
        layout.SetColumnSpan(layout.Controls[^1], 2);
        Controls.Add(layout);
        layout.BringToFront();
        Shown += (_, _) =>
        {
            _label.Focus();
            _label.SelectAll();
        };
    }

    public Revision BasedOn => (Revision)_basedOn.SelectedItem!;

    public string RevisionLabel => _label.Text.Trim();

    public string Note => _note.Text.Trim();
}
