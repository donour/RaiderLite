using RaiderLite.Core.Definitions;

namespace RaiderLite;

/// <summary>Lists the definition files searched when an image is opened (in order).</summary>
public sealed class DefinitionManagerForm : DialogBase
{
    /// <summary>Where definitions refreshed from the web are saved.</summary>
    public static string DownloadFolder => Path.Combine(AppSettings.SettingsDirectory, "Definitions");

    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub's API refuses requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RaiderLite");
        return client;
    });

    private readonly CancellationTokenSource _closing = new();
    private readonly Label _hint;
    private readonly Button _refresh;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, SelectionMode = SelectionMode.MultiExtended };

    public DefinitionManagerForm(IEnumerable<string> files) : base("Definition Manager")
    {
        HelpTopic = "definitions#the-definition-manager";
        FormBorderStyle = FormBorderStyle.Sizable;
        Size = new Size(700, 400);
        MinimumSize = new Size(450, 250);
        foreach (var f in files)
            _list.Items.Add(f);

        var side = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(6) };
        Button Add(string text, EventHandler click)
        {
            // Sized to the text (at least 110 wide): a fixed height clipped it at larger fonts and DPI.
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(110, 0) };
            b.Click += click;
            side.Controls.Add(b);
            return b;
        }
        Add("&Add...", (_, _) => AddFiles());
        Add("&Remove", (_, _) => { foreach (var item in _list.SelectedItems.Cast<object>().ToList()) _list.Items.Remove(item); });
        Add("Move &Up", (_, _) => MoveSelected(-1));
        Add("Move &Down", (_, _) => MoveSelected(1));
        _refresh = Add("Re&fresh from Web", async (_, _) => await RefreshFromWebAsync());
        new ToolTip().SetToolTip(_refresh, $"Download the latest definitions from {DefinitionDownloader.WebUrl}");

        _hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 36,
            Padding = new Padding(6, 6, 6, 0),
            Text = "Definitions are searched in this order; the first rom whose internal ID matches the image is used.",
        };

        Controls.Add(_list);
        Controls.Add(side);
        Controls.Add(_hint);
        _list.BringToFront();
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] dropped)
                AddPaths(dropped);
        };
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing.Cancel();
        base.OnFormClosed(e);
    }

    /// <summary>
    /// Downloads the definitions from the golden location into <see cref="DownloadFolder"/> and
    /// puts them at the top of the list.
    /// </summary>
    private async Task RefreshFromWebAsync()
    {
        var hint = _hint.Text;
        _refresh.Enabled = false;
        UseWaitCursor = true;
        _hint.Text = $"Downloading definitions from {DefinitionDownloader.WebUrl}...";
        try
        {
            var paths = await new DefinitionDownloader(Http.Value).DownloadAsync(DownloadFolder, _closing.Token);
            if (IsDisposed)
                return;
            for (int i = _list.Items.Count - 1; i >= 0; i--)
                if (paths.Contains((string)_list.Items[i], StringComparer.OrdinalIgnoreCase))
                    _list.Items.RemoveAt(i);
            for (int i = 0; i < paths.Count; i++)
                _list.Items.Insert(i, paths[i]);
            _list.ClearSelected();
            _list.TopIndex = 0;
            _hint.Text = hint;
            MessageBox.Show(this, $"Downloaded {paths.Count} definition file{(paths.Count == 1 ? "" : "s")} to {DownloadFolder}.",
                "Refresh Definitions", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The dialog was closed mid-download.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException
                                       or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            if (IsDisposed)
                return;
            _hint.Text = hint;
            var message = ex is TaskCanceledException ? "The download timed out." : ex.Message;
            MessageBox.Show(this, $"Couldn't refresh the definitions; the list is unchanged.\n\n{message}",
                "Refresh Definitions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                _refresh.Enabled = true;
                UseWaitCursor = false;
            }
        }
    }

    public List<string> Files => _list.Items.Cast<string>().ToList();

    private void AddFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add ECU Definitions",
            Filter = "RomRaider definitions (*.xml)|*.xml|All files (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = Directory.Exists(AppSettings.Current.LastDefinitionDir) ? AppSettings.Current.LastDefinitionDir : "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        AppSettings.Current.LastDefinitionDir = Path.GetDirectoryName(dialog.FileNames[0]) ?? "";
        AddPaths(dialog.FileNames);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(path, "*.xml");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    MessageBox.Show(this, ex.Message, "Add Definitions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                AddPaths(files);
                continue;
            }
            if (!_list.Items.Cast<string>().Contains(path, StringComparer.OrdinalIgnoreCase))
                _list.Items.Add(path);
        }
    }

    private void MoveSelected(int delta)
    {
        int i = _list.SelectedIndex;
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _list.Items.Count)
            return;
        var item = _list.Items[i];
        _list.Items.RemoveAt(i);
        _list.Items.Insert(j, item);
        _list.ClearSelected();
        _list.SelectedIndex = j;
    }
}
