namespace RaiderLite;

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
