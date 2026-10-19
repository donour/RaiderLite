using RaiderLite.Core.Tunes;

namespace RaiderLite;

/// <summary>
/// New Car and Import Image: a name, the image the car's history starts with and that first
/// revision's label ("Stock" for New Car, the image's file name when importing).
/// </summary>
public sealed class NewCarForm : DialogBase
{
    private readonly TextBox _name = new() { Width = 300 };
    private readonly TextBox _image = new() { Width = 300 };
    private readonly TextBox _label = new() { Width = 300 };
    private readonly bool _import;
    private readonly string _library;
    // The label as last filled in from the image's file name; replaced while the user hasn't changed it.
    private string _suggestedLabel = "";

    public NewCarForm(string imageFilter, string library, bool import = false, string? image = null) : base(import ? "Import Image" : "New Car")
    {
        _import = import;
        _library = library;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        OkButton.Text = import ? "Import" : "Create";
        _label.Text = import ? "" : "Stock";

        var browse = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(3, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = import ? "Choose the Image to Import" : "Choose the Stock Image", Filter = imageFilter };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                SetImage(dialog.FileName);
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
        Row(import ? "Image" : "Stock image", imageRow);
        Row("Car name", _name);
        Row("First revision", _label);
        layout.Controls.Add(new Label
        {
            Text = $"The car is a new folder in the tune library ({library}).\n" +
                   (import
                       ? "The image becomes its first revision, 000, which later revisions are based on;\nthe file itself isn't changed."
                       : "Its first revision, 000, is a copy of the stock image; the file itself isn't changed."),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 8, 3, 3),
        });
        layout.SetColumnSpan(layout.Controls[^1], 2);
        Controls.Add(layout);
        layout.BringToFront();
        if (image != null)
            SetImage(image);
        Shown += (_, _) => (image != null ? _name : _image).Focus();
    }

    public string CarName => _name.Text.Trim();

    public string ImagePath => _image.Text.Trim();

    public string FirstLabel => Car.TidyLabel(_label.Text);

    private void SetImage(string path)
    {
        _image.Text = path;
        var name = Path.GetFileNameWithoutExtension(path);
        if (_name.Text.Trim().Length == 0)
            _name.Text = name;
        if (_import && _label.Text == _suggestedLabel)
            _label.Text = _suggestedLabel = Car.CleanLabel(name);
    }

    protected override void OnOk(EventArgs e)
    {
        string? problem = !File.Exists(ImagePath) ? _import ? "Choose the image to import." : "Choose the stock image."
            : CarName.Length == 0 ? "Enter a name for the car."
            : Car.CleanLabel(CarName).Length == 0 ? "Use a name with letters or digits."
            : Car.CleanLabel(FirstLabel).Length == 0 ? "Enter a label for the first revision, such as Stock or the tune's name."
            : FolderInUse(Path.Combine(_library, Car.CleanLabel(CarName))) ? $"The tune library already has a folder named {Car.CleanLabel(CarName)}. Choose another name."
            : null;
        if (problem != null)
        {
            MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.None;
        }
    }

    private static bool FolderInUse(string folder)
    {
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;   // Creating the car will report it.
        }
    }
}
