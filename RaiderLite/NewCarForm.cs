using RaiderLite.Core.Tunes;

namespace RaiderLite;

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
