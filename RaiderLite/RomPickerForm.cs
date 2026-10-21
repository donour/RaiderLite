using RaiderLite.Core.Definitions;

namespace RaiderLite;

/// <summary>Picks a rom from a definition that didn't match the image (force load).</summary>
public sealed class RomPickerForm : DialogBase
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public RomPickerForm(DefinitionDocument doc, string imageName) : base("Choose ROM Definition")
    {
        HelpTopic = "definitions#no-match";
        FormBorderStyle = FormBorderStyle.Sizable;
        Size = new Size(520, 380);
        foreach (var rom in doc.Roms)
            _list.Items.Add(rom);
        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;
        _list.DoubleClick += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        var label = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 52,
            Padding = new Padding(6),
            Text = $"No rom in {doc} has an internal ID matching {imageName}.\nChoose one to load the image with anyway (tables may show garbage if it is the wrong one):",
        };
        Controls.Add(_list);
        Controls.Add(label);
        _list.BringToFront();
    }

    public RomDefinitionInfo? Selected => _list.SelectedItem as RomDefinitionInfo;
}
