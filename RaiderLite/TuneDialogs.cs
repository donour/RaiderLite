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

/// <summary>New Car: a name and the stock image it starts from.</summary>
public sealed class NewCarForm : DialogBase
{
    private readonly TextBox _name = new() { Width = 300 };
    private readonly TextBox _image = new() { Width = 300 };

    public NewCarForm(string imageFilter, string library) : base("New Car")
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        OkButton.Text = "Create";

        var browse = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(3, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Choose the Stock Image", Filter = imageFilter };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            _image.Text = dialog.FileName;
            if (_name.Text.Trim().Length == 0)
                _name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        };
        var imageRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        imageRow.Controls.Add(_image);
        imageRow.Controls.Add(browse);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
        void Row(string text, Control control)
        {
            layout.Controls.Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 3) });
            layout.Controls.Add(control);
        }
        Row("Car name", _name);
        Row("Stock image", imageRow);
        layout.Controls.Add(new Label
        {
            Text = $"The car is a new folder in the tune library ({library}).\nIts first revision, 000, is a copy of the stock image; the file itself isn't changed.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 8, 3, 3),
        });
        layout.SetColumnSpan(layout.Controls[^1], 2);
        Controls.Add(layout);
        layout.BringToFront();
        Shown += (_, _) => _name.Focus();
    }

    public string CarName => _name.Text.Trim();

    public string ImagePath => _image.Text.Trim();

    protected override void OnOk(EventArgs e)
    {
        string? problem = CarName.Length == 0 ? "Enter a name for the car."
            : Car.CleanLabel(CarName).Length == 0 ? "Use a name with letters or digits."
            : !File.Exists(ImagePath) ? "Choose the stock image."
            : null;
        if (problem != null)
        {
            MessageBox.Show(this, problem, "New Car", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.None;
        }
    }
}

/// <summary>A one-line question: a reason, a label...</summary>
public sealed class TextPromptForm : DialogBase
{
    private readonly TextBox _text = new() { Width = 360 };

    public TextPromptForm(string title, string prompt, string initial = "") : base(title)
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _text.Text = initial;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
        layout.Controls.Add(new Label { Text = prompt, AutoSize = true, Margin = new Padding(3, 3, 3, 6) });
        layout.Controls.Add(_text);
        Controls.Add(layout);
        layout.BringToFront();
        Shown += (_, _) =>
        {
            _text.Focus();
            _text.SelectAll();
        };
    }

    public string Value => _text.Text.Trim();
}
