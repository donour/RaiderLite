namespace RaiderLite;

/// <summary>Base for the small fixed dialogs: OK/Cancel buttons along the bottom.</summary>
public abstract class DialogBase : Form
{
    protected readonly FlowLayoutPanel ButtonPanel = new()
    {
        Dock = DockStyle.Bottom,
        FlowDirection = FlowDirection.RightToLeft,
        AutoSize = true,
        Padding = new Padding(8),
    };

    protected DialogBase(string title, bool cancel = true)
    {
        Text = title;
        Font = UiFonts.Regular;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;

        var ok = new Button { Text = cancel ? "OK" : "Close", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 0) };
        ok.Click += (_, e) => OnOk(e);
        ButtonPanel.Controls.Add(ok);
        AcceptButton = ok;
        OkButton = ok;
        if (cancel)
        {
            var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(80, 0) };
            ButtonPanel.Controls.Add(cancelButton);
            CancelButton = cancelButton;
        }
        else
        {
            CancelButton = ok;
        }
        Controls.Add(ButtonPanel);
    }

    /// <summary>The OK (or Close) button.</summary>
    protected Button OkButton { get; }

    protected virtual void OnOk(EventArgs e)
    {
    }
}
