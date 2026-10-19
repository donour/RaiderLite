using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using RaiderLite.Core.Tunes;

namespace RaiderLite;

/// <summary>
/// The left panel's Tunes tab: a car picker, a filter, the car's timeline of revisions and the
/// selected revision's details (notes, tables changed from its parent, files).
/// </summary>
internal sealed class TunesPanel : UserControl
{
    private readonly MainForm _main;
    private readonly CarRegistry _cars;
    private readonly ComboBox _carList = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _menuButton = new() { Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat };
    private readonly TextBox _filter = new() { Dock = DockStyle.Top, PlaceholderText = "Filter revisions, notes and tables", AccessibleName = "Filter revisions, notes and tables" };
    private readonly RevisionTimeline _timeline = new() { Dock = DockStyle.Fill };
    private readonly RevisionDetails _details = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.None };
    private readonly Panel _messagePanel = new() { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(12) };
    private readonly Label _message = new() { Dock = DockStyle.Top, AutoSize = false, Height = 80, UseMnemonic = false };
    private readonly Button _libraryButton = new() { Text = "Choose Library Folder...", AutoSize = true, Dock = DockStyle.Top };
    private readonly Button _newCarButton = new() { Text = "New Car...", AutoSize = true, Dock = DockStyle.Top };
    private readonly System.Windows.Forms.Timer _filterTimer = new() { Interval = 250 };
    private readonly HashSet<int> _expanded = [];
    private readonly Dictionary<int, string> _notes = [];
    private CarSession? _session;
    private bool _splitPlaced;
    private bool _changingCar;

    public TunesPanel(MainForm main, CarRegistry cars)
    {
        _main = main;
        _cars = cars;
        Font = UiFonts.Regular;

        _menuButton.FlatAppearance.BorderSize = 0;
        _menuButton.Image = IconFont.Render(IconFont.More, Px(16), Color.FromArgb(50, 54, 62));
        _menuButton.Width = Px(28);
        var tip = new ToolTip();
        tip.SetToolTip(_menuButton, "Tune library options");
        _menuButton.Click += (_, _) => ShowMenu(_menuButton, new Point(0, _menuButton.Height));

        var header = new Panel { Dock = DockStyle.Top, Height = _carList.PreferredHeight + Px(6), Padding = new Padding(0, Px(3), 0, Px(3)) };
        header.Controls.Add(_carList);
        header.Controls.Add(_menuButton);

        _split.Panel1.Controls.Add(_timeline);
        _split.Panel2.Controls.Add(_details);
        _split.SplitterMoved += (_, _) =>
        {
            if (_splitPlaced)
                AppSettings.Current.TimelineHeight = _split.SplitterDistance;
        };

        _messagePanel.Controls.Add(_libraryButton);
        _messagePanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Px(6) });
        _messagePanel.Controls.Add(_newCarButton);
        _messagePanel.Controls.Add(_message);
        _libraryButton.Click += (_, _) => ChooseLibraryFolder();
        _newCarButton.Click += (_, _) => _main.NewCar();

        Controls.Add(_split);
        Controls.Add(_messagePanel);
        Controls.Add(_filter);
        Controls.Add(header);

        _carList.SelectedIndexChanged += (_, _) =>
        {
            if (!_changingCar && _carList.SelectedItem is CarItem item)
                SelectCar(item.Folder);
        };
        _filter.TextChanged += (_, _) =>
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        };
        _filter.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                _filter.Clear();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                _timeline.Focus();
                e.SuppressKeyPress = true;
            }
        };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            RebuildTimeline();
        };

        _timeline.IsOpen = r => _main.IsRevisionOpen(r);
        _timeline.TooltipFor = Tooltip;
        _timeline.SelectedIndexChanged += (_, _) => ShowDetails();
        _timeline.RevisionActivated += async (_, r) => await _main.OpenRevisionAsync(r, showTables: true);
        _timeline.FoldActivated += (_, item) => Expand(item);
        _timeline.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
                ShowMenu(_timeline, e.Location);
        };
        _timeline.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && _timeline.SelectedTimelineItems.Any(i => i.Revision != null))
            {
                DeleteRevisions(_timeline.SelectedTimelineItems.Where(i => i.Revision != null).Select(i => i.Revision!).ToList());
                e.Handled = e.SuppressKeyPress = true;
            }
        };

        _details.OpenRequested += async (_, r) => await _main.OpenRevisionAsync(r, showTables: true);
        _details.ExpandRequested += (_, item) => Expand(item);
        _details.CompareTableRequested += async (_, request) =>
            await _main.OpenRevisionComparisonAsync(request.Revision, request.Other, request.Table);
        _details.NotesSaved += (_, r) => _notes[r.Number] = SafeReadNotes(r);
        _details.FilesDropped += (_, drop) => AddFiles(drop.Revision, drop.Paths);
        _details.AddFilesRequested += (_, r) => ChooseFiles(r);
        _details.ReportError = message => _main.SetStatus(message);
    }

    private sealed record CarItem(string Folder)
    {
        public override string ToString() => Path.GetFileName(Folder);
    }

    /// <summary>The car shown, if any.</summary>
    public Car? CurrentCar => _session?.Car;

    /// <summary>The revision selected in the timeline, when exactly one is.</summary>
    public Revision? SelectedRevision =>
        _timeline.SelectedTimelineItems.ToList() is [{ Revision: { } r }] ? r : null;

    private int Px(float v) => (int)Math.Round(v * DeviceDpi / 96f);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        // The splitter can only be placed once the panel has its real size.
        if (!_splitPlaced && _split.Height > Px(200))
        {
            int wanted = AppSettings.Current.TimelineHeight > 0 ? AppSettings.Current.TimelineHeight : _split.Height * 40 / 100;
            _split.SplitterDistance = Math.Clamp(wanted, Px(80), Math.Max(Px(80), _split.Height - Px(160)));
            _splitPlaced = true;
        }
    }

    // ----- cars -----

    /// <summary>Lists the library's cars again and shows <paramref name="select"/> (a car folder), else the last one used.</summary>
    public void ReloadCars(string? select = null)
    {
        CommitEdits();
        var library = AppSettings.Current.TuneLibraryFolder;
        IReadOnlyList<string> folders;
        try
        {
            folders = TuneLibrary.FindCars(library);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"The tune library {library} can't be read: {ex.Message}");
            return;
        }

        _changingCar = true;
        try
        {
            _carList.Items.Clear();
            foreach (var folder in folders)
                _carList.Items.Add(new CarItem(folder));
        }
        finally
        {
            _changingCar = false;
        }
        if (folders.Count == 0)
        {
            _session = null;
            ShowMessage(Directory.Exists(library)
                ? $"There are no cars in the tune library yet:\n{library}"
                : $"The tune library folder doesn't exist yet:\n{library}");
            return;
        }
        var wanted = select != null ? Path.GetFileName(Path.TrimEndingDirectorySeparator(select)) : AppSettings.Current.LastCar;
        var last = folders.FirstOrDefault(f => string.Equals(Path.GetFileName(f), wanted, StringComparison.OrdinalIgnoreCase));
        int index = last != null ? folders.ToList().IndexOf(last) : 0;
        if (_carList.SelectedIndex == index && _carList.SelectedItem is CarItem current)
            SelectCar(current.Folder);
        else
            _carList.SelectedIndex = index;
    }

    /// <summary>A revision was created: show it (in the shown car).</summary>
    public void OnRevisionAdded(Revision revision)
    {
        if (_session == null || !ReferenceEquals(revision.Car, _session.Car))
            return;
        _notes[revision.Number] = SafeReadNotes(revision);
        _filter.Clear();
        RebuildTimeline(select: [revision.Number]);
        _ = RefreshChangesAsync();
    }

    /// <summary>Selects a revision, switching car and unfolding, unhiding or unfiltering as needed.</summary>
    public void SelectRevision(Revision revision)
    {
        if (_session == null || !ReferenceEquals(revision.Car, _session.Car))
            ReloadCars(select: revision.Car.Folder);
        if (_session == null || !ReferenceEquals(revision.Car, _session.Car))
            return;
        if ((revision.Hidden || revision.Rejected != null) && !AppSettings.Current.ShowHiddenRevisions)
            AppSettings.Current.ShowHiddenRevisions = true;
        _filter.Clear();
        RebuildTimeline(select: [revision.Number]);
        if (_timeline.Items.Cast<TimelineItem>().FirstOrDefault(i => i.IsFold && i.Members.Contains(revision)) is { Row.Fold: { } fold })
        {
            _expanded.Add(fold.Parent);
            RebuildTimeline(select: [revision.Number]);
        }
        _timeline.Focus();
    }

    private void SelectCar(string folder)
    {
        CommitEdits();
        Car car;
        try
        {
            car = _cars.Open(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            _session = null;
            ShowMessage($"{Path.GetFileName(folder)} can't be opened:\n{ex.Message}");
            return;
        }
        AppSettings.Current.LastCar = Path.GetFileName(folder);
        _session = new CarSession(car);
        _expanded.Clear();
        _notes.Clear();
        foreach (var r in car.Revisions)
            _notes[r.Number] = SafeReadNotes(r);
        _messagePanel.Visible = false;
        _split.Visible = true;
        RebuildTimeline(select: []);
        if (_timeline.Items.Count > 0)
            _timeline.SetSelected(0, true);
        _ = RefreshChangesAsync();
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _split.Visible = false;
        _messagePanel.Visible = true;
        _timeline.SetItems([]);
    }

    private async Task RefreshChangesAsync()
    {
        if (_session is not { } session)
            return;
        if (!await session.RefreshAsync(_main.FindDefinition) || !ReferenceEquals(session, _session))
            return;
        if (session.Problem != null)
            _main.SetStatus(session.Problem);
        if (_filter.Text.Trim().Length > 0)
            RebuildTimeline();
        ShowDetails();
    }

    /// <summary>An image of the shown car was saved: its changes (and its children's) are different now.</summary>
    public void OnImageSaved(Revision revision)
    {
        if (_session != null && ReferenceEquals(revision.Car, _session.Car))
        {
            // Saving may have started the notes (one line per changed table).
            _notes[revision.Number] = SafeReadNotes(revision);
            _details.ReloadNotes(revision);
            _timeline.Invalidate();
            _ = RefreshChangesAsync();
        }
    }

    /// <summary>Images were opened or closed: redraw which revisions are open.</summary>
    public void RefreshMarks() => _timeline.Invalidate();

    /// <summary>Saves notes being typed.</summary>
    public void CommitEdits() => _details.CommitNotes();

    private void ChooseLibraryFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the tune library folder: each car is a folder in it.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(AppSettings.Current.TuneLibraryFolder) ? AppSettings.Current.TuneLibraryFolder : "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        AppSettings.Current.TuneLibrary = dialog.SelectedPath;
        AppSettings.Current.Save();
        ReloadCars();
    }

    // ----- timeline -----

    private void RebuildTimeline(IReadOnlyCollection<int>? select = null)
    {
        if (_session is not { } session)
            return;
        var car = session.Car;
        bool showHidden = AppSettings.Current.ShowHiddenRevisions;
        var words = _filter.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<TimelineItem> items;
        if (words.Length > 0)
        {
            // A search shows the matching revisions as a plain list, newest first.
            items = car.Revisions
                .Where(r => (showHidden || !r.Hidden && r.Rejected == null) && words.All(w => Matches(r, w)))
                .OrderByDescending(r => r.Number)
                .Select(r => new TimelineItem(new TimelineRow(r.Number, 0, false, false, [], []), r, []))
                .ToList();
        }
        else
        {
            var rows = TimelineLayout.Build(TimelineLayout.NodesFor(car, showHidden), DateTime.Now, _expanded);
            items = rows.Select(row => row.Fold is { } fold
                    ? new TimelineItem(row, null, fold.Members.Select(n => car.Get(n)!).ToList())
                    : new TimelineItem(row, car.Get(row.Number), []))
                .ToList();
        }
        _timeline.SetItems(items, select);
        ShowDetails();
    }

    private bool Matches(Revision r, string word)
    {
        bool Has(string? s) => s != null && s.Contains(word, StringComparison.CurrentCultureIgnoreCase);
        if (Has(r.Label) || Has(r.Number.ToString("000", CultureInfo.InvariantCulture)) || Has(r.Folder) || Has(r.Rejected) || Has(r.ImportedFrom))
            return true;
        if (_notes.TryGetValue(r.Number, out var notes) && Has(notes))
            return true;
        return _session?.ChangesOf(r)?.Comparison?.Tables.Any(t => Has(t.Table.Name)) == true;
    }

    private void Expand(TimelineItem fold)
    {
        if (fold.Row.Fold is not { } f)
            return;
        _expanded.Add(f.Parent);
        RebuildTimeline(select: [f.Members[0]]);
    }

    private void ShowDetails()
    {
        if (_session is not { } session)
        {
            _details.ShowNothing("");
            return;
        }
        var selected = _timeline.SelectedTimelineItems.ToList();
        if (selected.Count == 1 && selected[0].Revision is { } r)
            _details.ShowRevision(session, r);
        else if (selected.Count == 1)
            _details.ShowFold(selected[0]);
        else if (selected.Count == 2 && selected.All(i => i.Revision != null))
        {
            var pair = selected.Select(i => i.Revision!).OrderBy(x => x.Number).ToList();
            _details.ShowComparison(session, pair[0], pair[1]);
        }
        else
            _details.ShowNothing(selected.Count == 0 ? "Select a revision." : "Select one revision, or two to compare them.");
    }

    private string Tooltip(TimelineItem item)
    {
        if (item.IsFold)
        {
            return $"{item.Members.Count} revisions in side branches that are finished:\n" +
                   string.Join("\n", item.Members.Take(12).Select(m => $"{m.Number:000}  {m.Label}")) +
                   (item.Members.Count > 12 ? "\n..." : "") + "\nDouble-click to show them.";
        }
        var r = item.Revision!;
        var car = r.Car;
        var sb = new StringBuilder();
        sb.Append(r.Number.ToString("000", CultureInfo.InvariantCulture)).Append("  ").AppendLine(r.Label);
        sb.Append(r.Created.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture));
        if (car.ParentOf(r) is { } parent)
            sb.Append($" · based on {parent.Number:000}");
        sb.AppendLine();
        if (r.Rejected != null)
            sb.AppendLine($"Rejected: {r.Rejected}");
        if (car.IsNoChange(r))
            sb.AppendLine("Same image as the revision it is based on.");
        else if (_session?.ChangesOf(r)?.Comparison is { } c && c.Tables.Count > 0)
            sb.AppendLine($"Changed: {string.Join(", ", c.Tables.Take(4).Select(t => t.Table.Name))}" + (c.Tables.Count > 4 ? $" and {c.Tables.Count - 4} more" : ""));
        if (_notes.TryGetValue(r.Number, out var notes) && notes.Trim().Length > 0)
        {
            var lines = notes.Trim().Split('\n').Select(l => l.TrimEnd('\r')).Take(4).ToList();
            sb.AppendLine(string.Join("\n", lines));
        }
        return sb.ToString().TrimEnd();
    }

    private void ShowMenu(Control anchor, Point location)
    {
        var menu = new ContextMenuStrip();
        var selected = _timeline.SelectedTimelineItems.ToList();
        if (anchor == _timeline && selected.Count == 1 && selected[0].Revision is { } r)
        {
            var car = r.Car;
            var open = new ToolStripMenuItem("&Open", null, async (_, _) => await _main.OpenRevisionAsync(r, showTables: true)) { Font = UiFonts.Bold };
            menu.Items.Add(open);
            menu.Items.Add("&New Revision from This...", null, async (_, _) => await _main.NewRevisionAsync(r));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Re&name...", null, (_, _) => Rename(r));
            if (r.Number != 0)
            {
                if (r.Rejected == null)
                    menu.Items.Add("Re&ject...", null, (_, _) => Reject(r));
                else
                    menu.Items.Add("Clear Re&jection", null, (_, _) => Change(r, () => car.SetRejected(r, null)));
                menu.Items.Add(r.Hidden ? "Un&hide" : "&Hide", null, (_, _) => Change(r, () => car.SetHidden(r, !r.Hidden)));
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("&Copy Changelog", null, (_, _) => CopyChangelog(r));
            menu.Items.Add("Add &Files...", null, (_, _) => ChooseFiles(r));
            menu.Items.Add("Show in &Explorer", null, (_, _) => ShowInExplorer(File.Exists(r.ImagePath) ? r.ImagePath : r.FolderPath));
            if (r.Number != 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("&Delete...", null, (_, _) => DeleteRevisions([r]));
            }
            menu.Items.Add(new ToolStripSeparator());
        }
        else if (anchor == _timeline && selected.Count > 1 && selected.All(i => i.Revision != null) && selected.Any(i => i.Revision!.Number != 0))
        {
            var revisions = selected.Select(i => i.Revision!).ToList();
            menu.Items.Add($"&Delete {revisions.Count(r => r.Number != 0)} Revisions...", null, (_, _) => DeleteRevisions(revisions));
            menu.Items.Add(new ToolStripSeparator());
        }
        else if (anchor == _timeline && selected.Count == 1 && selected[0].IsFold)
        {
            var fold = selected[0];
            menu.Items.Add("&Show Folded Revisions", null, (_, _) => Expand(fold));
            menu.Items.Add(new ToolStripSeparator());
        }
        if (_session != null && _expanded.Count > 0)
        {
            menu.Items.Add("&Fold Finished Side Branches", null, (_, _) =>
            {
                _expanded.Clear();
                RebuildTimeline();
            });
        }
        var showHidden = new ToolStripMenuItem("Show &Hidden and Rejected Revisions") { Checked = AppSettings.Current.ShowHiddenRevisions };
        showHidden.Click += (_, _) =>
        {
            AppSettings.Current.ShowHiddenRevisions = !AppSettings.Current.ShowHiddenRevisions;
            RebuildTimeline();
        };
        menu.Items.Add(showHidden);
        if (anchor == _menuButton)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("New &Revision...", null, async (_, _) => await _main.NewRevisionAsync(SelectedRevision)).Enabled = SelectedRevision != null;
            menu.Items.Add("New Ca&r...", null, (_, _) => _main.NewCar());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("&Refresh", null, (_, _) =>
            {
                if (_session != null)
                    _cars.Forget(_session.Car.Folder);
                ReloadCars();
            });
            if (_session != null)
            {
                var folder = _session.Car.Folder;
                menu.Items.Add("Open &Car Folder", null, (_, _) => ShowInExplorer(folder));
            }
            menu.Items.Add("Open &Library Folder", null, (_, _) =>
            {
                if (Directory.Exists(AppSettings.Current.TuneLibraryFolder))
                    ShowInExplorer(AppSettings.Current.TuneLibraryFolder);
            });
            menu.Items.Add("Choose Library &Folder...", null, (_, _) => ChooseLibraryFolder());
        }
        MainForm.ShowAndDispose(menu, anchor, location);
    }

    // ----- revision actions -----

    /// <summary>Runs a change of a revision's record, then shows the result.</summary>
    private void Change(Revision revision, Action change)
    {
        try
        {
            change();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, revision.ToString(), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RebuildTimeline(select: [revision.Number]);
        // Hidden or rejected out of the timeline: select the revision it was based on instead.
        if (_timeline.SelectedItems.Count == 0 && revision.Parent is int parent)
            RebuildTimeline(select: [parent]);
    }

    /// <summary>
    /// Deletes revisions after confirming: their folders go to the Recycle Bin and the revisions
    /// based on them are based on the nearest surviving ancestor instead. Stock is never deleted.
    /// </summary>
    private void DeleteRevisions(IEnumerable<Revision> selection)
    {
        // Newest first, so a deleted revision's children are re-based before it goes.
        var revisions = selection.Where(r => r.Number != 0).OrderByDescending(r => r.Number).ToList();
        if (revisions.Count == 0)
            return;
        CommitEdits();
        var car = revisions[0].Car;
        var deleted = revisions.ToHashSet();
        Revision? Survivor(Revision r)
        {
            var p = car.ParentOf(r);
            while (p != null && deleted.Contains(p))
                p = car.ParentOf(p);
            return p;
        }
        var rebased = car.Revisions.Where(r => !deleted.Contains(r) && car.ParentOf(r) is { } p && deleted.Contains(p)).ToList();
        int files = revisions.Sum(r => car.Attachments(r).Count);

        var text = (revisions.Count == 1 ? "Its folder" : "Their folders") + " (image, notes" +
                   (files > 0 ? $" and {files} other file{(files == 1 ? "" : "s")}" : "") + ") will go to the Recycle Bin.";
        if (revisions.Count > 1)
            text = string.Join("\n", revisions.OrderBy(r => r.Number).Select(r => r.ToString())) + "\n\n" + text;
        if (rebased.Count > 0)
            text += "\n\n" + string.Join("\n", rebased.Select(r => $"{r.Number:000} will be based on {Survivor(r)?.ToString() ?? "nothing"} instead."));
        var delete = new TaskDialogButton("&Delete");
        var cancel = TaskDialogButton.Cancel;
        var page = new TaskDialogPage
        {
            Caption = "Delete Revision",
            Heading = revisions.Count == 1 ? $"Delete {revisions[0]}?" : $"Delete {revisions.Count} revisions?",
            Text = text,
            Icon = TaskDialogIcon.Warning,
            AllowCancel = true,
            Buttons = { delete, cancel },
            DefaultButton = cancel,
        };
        if (TaskDialog.ShowDialog(this, page) != delete)
            return;

        var select = Survivor(revisions[^1]);
        int done = 0;
        foreach (var revision in revisions)
        {
            if (!_main.CloseRevisionImages(revision))
                break;
            try
            {
                car.DeleteRevision(revision, path =>
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin));
                _notes.Remove(revision.Number);
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                MessageBox.Show(this, $"{revision} couldn't be deleted:\n\n{ex.Message}", "Delete Revision", MessageBoxButtons.OK, MessageBoxIcon.Error);
                break;
            }
        }
        _main.OnRevisionsChanged();
        RebuildTimeline(select: select != null ? [select.Number] : []);
        _ = RefreshChangesAsync();
        _main.SetStatus(done == 1 ? $"Deleted {revisions[0]} (its folder is in the Recycle Bin)." : $"Deleted {done} revisions (their folders are in the Recycle Bin).");
    }

    private void Rename(Revision revision)
    {
        using var prompt = new TextPromptForm("Rename Revision", $"New label for {revision.Number:000} (its folder is renamed to match):", revision.Label);
        if (prompt.ShowDialog(this) != DialogResult.OK || prompt.Value == revision.Label)
            return;
        CommitEdits();
        _main.RelabelRevision(revision, prompt.Value);
        RebuildTimeline(select: [revision.Number]);
    }

    private void Reject(Revision revision)
    {
        using var prompt = new TextPromptForm("Reject Revision", $"Why is {revision} rejected? (e.g. too lean)", "");
        if (prompt.ShowDialog(this) != DialogResult.OK)
            return;
        Change(revision, () => revision.Car.SetRejected(revision, prompt.Value.Length > 0 ? prompt.Value : "rejected"));
        if (!AppSettings.Current.ShowHiddenRevisions)
            _main.SetStatus($"{revision} is rejected and hidden from the timeline (⋯ > Show Hidden and Rejected Revisions).");
    }

    private void CopyChangelog(Revision revision)
    {
        CommitEdits();
        try
        {
            Clipboard.SetText(Changelog.Build(revision.Car, revision, SafeReadNotes));
            _main.SetStatus($"Copied the changelog of {revision} ({revision.Car.Lineage(revision).Count} revisions) to the clipboard.");
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            _main.SetStatus($"The clipboard is in use: {ex.Message}");
        }
    }

    private void ChooseFiles(Revision revision)
    {
        using var dialog = new OpenFileDialog { Title = $"Add Files to {revision}", Multiselect = true, Filter = "All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            AddFiles(revision, dialog.FileNames);
    }

    /// <summary>Copies files (logs, flash files...) into the revision's folder.</summary>
    private void AddFiles(Revision revision, IReadOnlyList<string> paths)
    {
        int added = 0;
        foreach (var path in paths.Where(File.Exists))
        {
            try
            {
                revision.Car.AddAttachment(revision, path);
                added++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"{Path.GetFileName(path)} couldn't be added:\n\n{ex.Message}", "Add Files", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        _main.SetStatus($"Added {added} file(s) to {revision}.");
        ShowDetails();
    }

    internal static void ShowInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // No shell to show it in; nothing to do.
        }
    }

    private static string SafeReadNotes(Revision r)
    {
        try
        {
            return r.Car.ReadNotes(r);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _filterTimer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// The selected revision's details: notes (editable), the tables it changed from its parent and
/// its files; or a comparison of two revisions, or the contents of a fold.
/// </summary>
internal sealed class RevisionDetails : UserControl
{
    private static readonly Color Muted = Color.FromArgb(96, 100, 108);
    private static readonly Color Warning = Color.FromArgb(176, 88, 0);

    // The sections in a scrolling pane: when even their smallest sizes don't fit, it scrolls
    // rather than letting them draw over each other.
    private readonly Panel _scroller = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly TableLayoutPanel _layout = new() { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 9, Padding = new Padding(4, 4, 4, 2), AutoSizeMode = AutoSizeMode.GrowAndShrink };
    private bool _fitting;
    private readonly Label _title = new() { AutoSize = true, Font = UiFonts.Heading, UseMnemonic = false, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _meta = new() { AutoSize = true, ForeColor = Muted, UseMnemonic = false, Margin = new Padding(0, 0, 0, 6) };
    private readonly Label _notesCaption = Caption("Notes");
    private readonly TextBox _notes = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, AccessibleName = "Notes" };
    private readonly Label _changesCaption = Caption("");
    private readonly LinkLabel _addMissing = new() { AutoSize = true, Visible = false, Margin = new Padding(8, 6, 0, 2), UseMnemonic = false };
    private readonly FlowLayoutPanel _changesHeader = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0), Dock = DockStyle.Fill };
    private readonly ListView _changes = List("Table", "Cells");
    private readonly Label _filesCaption = Caption("Files");
    private readonly ListView _files = List("File", "Size");
    private readonly FlowLayoutPanel _buttons = new() { AutoSize = true, Dock = DockStyle.Bottom, WrapContents = true, Padding = new Padding(2) };
    private readonly ToolTip _metaTip = new();
    private readonly Button _open = new() { Text = "&Open", AutoSize = true };
    private readonly Button _explorer = new() { Text = "Show in E&xplorer", AutoSize = true };
    private readonly Button _expand = new() { Text = "&Show Folded Revisions", AutoSize = true };
    private Revision? _revision;        // whose notes are in the box
    private Revision? _compareOther;    // the revision the changes list compares against
    private Revision? _compareSubject;
    private TimelineItem? _fold;
    private bool _hints;                // mark tables the notes don't mention (one revision shown)
    private bool _showNotes, _showChanges, _showChangeList, _showFiles, _showFileList;
    private readonly ToolTip _hintTip = new();

    public RevisionDetails()
    {
        Font = UiFonts.Regular;
        foreach (var style in new[] { 0, 0, 0, 100, 0, 0, 0, 0, 0 })
            _layout.RowStyles.Add(style == 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, style));
        _layout.Controls.Add(_title, 0, 0);
        _layout.Controls.Add(_meta, 0, 1);
        _layout.Controls.Add(_notesCaption, 0, 2);
        _layout.Controls.Add(_notes, 0, 3);
        _changesHeader.Controls.Add(_changesCaption);
        _changesHeader.Controls.Add(_addMissing);
        _layout.Controls.Add(_changesHeader, 0, 4);
        _layout.Controls.Add(_changes, 0, 5);
        _layout.Controls.Add(_filesCaption, 0, 6);
        _layout.Controls.Add(_files, 0, 7);
        _buttons.Controls.AddRange([_open, _explorer, _expand]);
        _scroller.Controls.Add(_layout);
        // The buttons stay in view below the sections, which scroll when they don't fit.
        Controls.Add(_scroller);
        Controls.Add(_buttons);

        _notes.Leave += (_, _) => CommitNotes();
        _notes.TextChanged += (_, _) => UpdateHints();
        _addMissing.LinkClicked += (_, _) => AddMissingToNotes();
        foreach (var target in new Control[] { this, _layout, _files, _changes, _notes })
        {
            target.AllowDrop = true;
            target.DragEnter += (_, e) => e.Effect = _revision != null && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
            target.DragDrop += (_, e) =>
            {
                if (_revision != null && e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                    FilesDropped?.Invoke(this, (_revision, paths));
            };
        }
        _open.Click += (_, _) =>
        {
            if (_revision != null)
                OpenRequested?.Invoke(this, _revision);
        };
        _explorer.Click += (_, _) =>
        {
            if (_revision != null)
                TunesPanel.ShowInExplorer(File.Exists(_revision.ImagePath) ? _revision.ImagePath : _revision.FolderPath);
        };
        _expand.Click += (_, _) =>
        {
            if (_fold != null)
                ExpandRequested?.Invoke(this, _fold);
        };
        _changes.ItemActivate += (_, _) =>
        {
            if (_changes.SelectedItems.Count == 1 && _changes.SelectedItems[0].Tag is string table && _compareSubject != null && _compareOther != null)
                CompareTableRequested?.Invoke(this, (_compareSubject, _compareOther, table));
        };
        _files.ItemActivate += (_, _) =>
        {
            if (_files.SelectedItems.Count == 1 && _files.SelectedItems[0].Tag is string path)
                OpenFile(path);
        };
        _files.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || _revision is not { } revision)
                return;
            var menu = new ContextMenuStrip();
            if (_files.GetItemAt(e.X, e.Y)?.Tag is string path)
            {
                menu.Items.Add("&Open", null, (_, _) => OpenFile(path));
                menu.Items.Add("Show in E&xplorer", null, (_, _) => TunesPanel.ShowInExplorer(path));
                menu.Items.Add(new ToolStripSeparator());
            }
            menu.Items.Add("&Add Files...", null, (_, _) => AddFilesRequested?.Invoke(this, revision));
            MainForm.ShowAndDispose(menu, _files, e.Location);
        };
        Resize += (_, _) =>
        {
            FitLabels();
            FitRows();
        };
        VisibleChanged += (_, _) =>
        {
            FitLabels();
            FitRows();
        };
        // A scrollbar coming or going changes the width the labels wrap in.
        _scroller.ClientSizeChanged += (_, _) =>
        {
            FitLabels();
            FitRows();
        };
        ShowNothing("");
    }

    public event EventHandler<Revision>? OpenRequested;
    public event EventHandler<TimelineItem>? ExpandRequested;
    public event EventHandler<(Revision Revision, Revision Other, string Table)>? CompareTableRequested;
    public event EventHandler<Revision>? NotesSaved;
    public event EventHandler<(Revision Revision, string[] Paths)>? FilesDropped;
    public event EventHandler<Revision>? AddFilesRequested;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string>? ReportError { get; set; }

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiFonts.Bold,
        UseMnemonic = false,
        Margin = new Padding(0, 6, 0, 2),
    };

    private static ListView List(string first, string second)
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.None,
            MultiSelect = false,
            ShowItemToolTips = true,
        };
        list.Columns.Add(first, 200);
        list.Columns.Add(second, 72, HorizontalAlignment.Right);
        // The first column takes what the second leaves, short of a vertical scrollbar, so no horizontal one appears.
        list.Resize += (_, _) => list.Columns[0].Width = Math.Max(60, list.ClientSize.Width - list.Columns[1].Width - SystemInformation.VerticalScrollBarWidth);
        return list;
    }

    private void FitLabels()
    {
        int width = Math.Max(50, _scroller.ClientSize.Width - _layout.Padding.Horizontal - 4);
        _title.MaximumSize = new Size(width, 0);
        _meta.MaximumSize = new Size(width, 0);
        _changesCaption.MaximumSize = new Size(width, 0);
    }

    /// <summary>
    /// Shows or hides the notes, changes and files sections; a hidden section's row collapses.
    /// The lists are as tall as their rows (up to a few, then they scroll) and the notes take the
    /// rest. Files can show just their caption ("No other files") without a list.
    /// </summary>
    private void Sections(bool notes, bool changes, bool files, bool fileList = true)
    {
        // Kept apart from Visible, which reads false while the Tunes tab itself is hidden.
        _showNotes = notes;
        _showChanges = changes;
        _showChangeList = changes && _changes.Items.Count > 0;
        _showFiles = files;
        _showFileList = files && fileList;
        _notesCaption.Visible = _notes.Visible = notes;
        _changesHeader.Visible = changes;
        _changes.Visible = _showChangeList;
        _filesCaption.Visible = files;
        _files.Visible = _showFileList;
        FitRows();
    }

    /// <summary>
    /// Sizes the section rows: each list as tall as its rows (a few at most, then it scrolls), the
    /// notes the rest but never under four lines; when that doesn't fit, the lists give way
    /// (keeping three rows of changes and one of files). Without notes the changes list takes the
    /// rest. When even those smallest sizes don't fit, the sections keep them and the pane scrolls.
    /// </summary>
    private void FitRows()
    {
        // While hidden the panel has no real size; it fits again when shown.
        if (!IsHandleCreated || !Visible || _fitting)
            return;
        _fitting = true;
        try
        {
            int viewport = _scroller.ClientSize.Height;
            // Measured at the pane's width: the labels wrap.
            int width = Math.Max(50, _scroller.ClientSize.Width - _layout.Padding.Horizontal);
            var shown = new List<Control> { _title, _meta };
            if (_showNotes)
                shown.Add(_notesCaption);
            if (_showChanges)
                shown.Add(_changesHeader);
            if (_showFiles)
                shown.Add(_filesCaption);
            int fixedRows = _layout.Padding.Vertical
                + shown.Sum(c => c.GetPreferredSize(new Size(width, 0)).Height + c.Margin.Vertical)
                + (_showNotes ? _notes.Margin.Vertical : 0)
                + (_showChangeList ? _changes.Margin.Vertical : 0)
                + (_showFileList ? _files.Margin.Vertical : 0);
            int notesMin = _showNotes ? _notes.Font.Height * 4 + 8 : 0;
            int changes = _showChangeList ? ListHeight(_changes, 8) : 0;
            int files = _showFileList ? ListHeight(_files, 4) : 0;
            int minChanges = _showChangeList ? ListHeight(_changes, 3) : 0;
            int minFiles = _showFileList ? ListHeight(_files, 1) : 0;
            int room = viewport - fixedRows - notesMin;
            bool fits = minChanges + minFiles <= room;
            if (!fits)
            {
                changes = minChanges;
                files = minFiles;
            }
            else if (changes + files > room)
            {
                double scale = room / (double)(changes + files);
                changes = Math.Max(minChanges, (int)(changes * scale));
                files = Math.Max(minFiles, (int)(files * scale));
            }

            // The spare height goes to the notes, else (comparing) to the changes list.
            var styles = _layout.RowStyles;
            if (_showNotes)
            {
                styles[3] = fits ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, notesMin);
                styles[5] = new RowStyle(SizeType.Absolute, changes);
            }
            else
            {
                styles[3] = new RowStyle(SizeType.Absolute, 0);
                styles[5] = fits && _showChangeList ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, changes);
            }
            styles[7] = new RowStyle(SizeType.Absolute, files);
            if (fits)
            {
                _layout.AutoSize = false;
                _layout.Height = viewport;
            }
            else
            {
                _layout.AutoSize = true;
            }
        }
        finally
        {
            _fitting = false;
        }
    }

    /// <summary>The height showing a list's rows, at least one and at most <paramref name="maxRows"/>.</summary>
    private static int ListHeight(ListView list, int maxRows)
    {
        int rowHeight = list.Items.Count > 0 ? list.GetItemRect(0).Height : list.Font.Height + 6;
        return Math.Clamp(list.Items.Count, 1, maxRows) * rowHeight + 2 * SystemInformation.Border3DSize.Height + 4;
    }

    public void ShowNothing(string hint)
    {
        CommitNotes();
        _hints = false;
        _revision = _compareOther = _compareSubject = null;
        _fold = null;
        _title.Text = "";
        _meta.Text = hint;
        Sections(false, false, false);
        _open.Visible = _explorer.Visible = _expand.Visible = false;
    }

    public void ShowRevision(CarSession session, Revision revision)
    {
        if (!ReferenceEquals(revision, _revision))
        {
            CommitNotes();
            _revision = revision;
            try
            {
                _notes.Text = revision.Car.ReadNotes(revision).ReplaceLineEndings("\r\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _notes.Text = "";
                ReportError?.Invoke($"The notes of {revision} can't be read: {ex.Message}");
            }
            _notes.Modified = false;
            _notes.SelectionStart = 0;
        }
        _fold = null;
        var car = revision.Car;
        _title.Text = revision.ToString();
        _meta.Text = Describe(revision);
        // Where an imported revision came from is there on demand; it would take two lines.
        _metaTip.SetToolTip(_meta, revision.ImportedFrom != null ? $"Imported from {revision.ImportedFrom}" : "");
        _meta.ForeColor = car.CheckImage(revision) == ImageState.Ok ? Muted : Warning;

        var parent = car.ParentOf(revision);
        _compareSubject = revision;
        _compareOther = parent;
        _hints = true;
        if (parent == null)
            FillChanges(null, revision.Number == 0 ? "Stock: nothing to compare with." : "Not based on another revision.");
        else if (car.IsNoChange(revision))
            FillChanges(null, $"No change from {parent.Number:000}: the image is identical.");
        else if (session.Problem != null)
            FillChanges(null, session.Problem);
        else if (session.ChangesOf(revision) is not { } changes)
            FillChanges(null, session.Loaded ? "Changes aren't known yet." : "Working out the changes...");
        else if (changes.Problem != null)
            FillChanges(null, changes.Problem);
        else
            FillChanges(changes.Comparison, $"Changed from {parent.Number:000}");

        _files.BeginUpdate();
        _files.Items.Clear();
        try
        {
            foreach (var path in car.Attachments(revision))
            {
                long size = new FileInfo(path).Length;
                _files.Items.Add(new ListViewItem([Path.GetFileName(path), FormatSize(size)]) { Tag = path, ToolTipText = path });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportError?.Invoke($"The files of {revision} can't be listed: {ex.Message}");
        }
        _files.EndUpdate();
        _filesCaption.Text = _files.Items.Count == 0 ? "No other files" : $"Files ({_files.Items.Count})";

        Sections(notes: true, changes: true, files: true, fileList: _files.Items.Count > 0);
        _open.Visible = _explorer.Visible = true;
        _expand.Visible = false;
    }

    public void ShowComparison(CarSession session, Revision older, Revision newer)
    {
        CommitNotes();
        _hints = false;
        _revision = null;
        _fold = null;
        _title.Text = $"{older.Number:000} and {newer.Number:000}";
        _meta.Text = $"{older}\n{newer}";
        _meta.ForeColor = Muted;
        _compareSubject = newer;
        _compareOther = older;
        var changes = session.Compare(older, newer);
        if (changes == null)
            FillChanges(null, session.Problem ?? "Working out the changes...");
        else if (changes.Problem != null)
            FillChanges(null, changes.Problem);
        else if (changes.Comparison!.Identical)
            FillChanges(null, "The images are identical.");
        else
            FillChanges(changes.Comparison, $"Differences from {older.Number:000}");
        Sections(notes: false, changes: true, files: false);
        _open.Visible = _explorer.Visible = _expand.Visible = false;
    }

    public void ShowFold(TimelineItem fold)
    {
        CommitNotes();
        _hints = false;
        _revision = _compareOther = _compareSubject = null;
        _fold = fold;
        _title.Text = $"{fold.Members.Count} folded revisions";
        _meta.Text = "Side branches with nothing new for a week:\n" +
                     string.Join("\n", fold.Members.Take(20).Select(m => $"{m.Number:000}  {RevisionTimeline.ShortDate(m.Created)}  {m.Label}")) +
                     (fold.Members.Count > 20 ? "\n..." : "");
        _meta.ForeColor = Muted;
        Sections(false, false, false);
        _open.Visible = _explorer.Visible = false;
        _expand.Visible = true;
    }

    private void FillChanges(Core.Model.ImageComparison? comparison, string caption)
    {
        _changes.BeginUpdate();
        _changes.Items.Clear();
        if (comparison != null)
        {
            caption += comparison.Tables.Count == 1 ? ": 1 table" : $": {comparison.Tables.Count} tables";
            if (comparison.ChangedBytesOutsideTables > 0)
                caption += $", and {comparison.ChangedBytesOutsideTables} byte(s) no table covers";
            foreach (var change in comparison.Tables)
            {
                _changes.Items.Add(new ListViewItem([change.Table.Name, change.ChangedCells.ToString(CultureInfo.CurrentCulture)])
                {
                    Tag = change.Table.Name,
                    ToolTipText = $"{change.Table.Category}\nDouble-click to open both versions side by side.",
                });
            }
        }
        _changes.EndUpdate();
        _changesCaption.Text = caption;
        UpdateHints();
    }

    /// <summary>
    /// Marks changed tables the notes don't seem to mention (⚠, in orange) and offers to add a
    /// line for each. Follows the notes as they are typed.
    /// </summary>
    private void UpdateHints()
    {
        var missing = new List<string>();
        _changes.BeginUpdate();
        foreach (ListViewItem item in _changes.Items)
        {
            var name = (string)item.Tag!;
            bool mentioned = !_hints || Changelog.Mentions(_notes.Text, name);
            if (!mentioned)
                missing.Add(name);
            var text = mentioned ? name : "⚠ " + name;
            if (item.Text != text)
                item.Text = text;
            item.ForeColor = mentioned ? _changes.ForeColor : Warning;
        }
        _changes.EndUpdate();
        _addMissing.Text = missing.Count == 1 ? "Add 1 to the notes" : $"Add {missing.Count} to the notes";
        _addMissing.Visible = missing.Count > 0;
        _hintTip.SetToolTip(_addMissing,"Not mentioned in the notes:\n" + string.Join("\n", missing));
    }

    /// <summary>Adds a "* table: " line for each changed table the notes don't mention, ready to complete.</summary>
    private void AddMissingToNotes()
    {
        if (_revision == null)
            return;
        var names = _changes.Items.Cast<ListViewItem>().Select(i => (string)i.Tag!).ToList();
        var lines = Changelog.SuggestedLines(_notes.Text, names).ReplaceLineEndings("\r\n");
        if (lines.Length == 0)
            return;
        var text = _notes.Text.TrimEnd();
        _notes.Text = (text.Length > 0 ? text + "\r\n" : "") + lines;
        _notes.Modified = true;
        CommitNotes();
        _notes.Focus();
        _notes.SelectionStart = _notes.Text.Length;
        _notes.ScrollToCaret();
    }

    /// <summary>The notes changed on disk (started by a save): show them, unless they are being edited.</summary>
    public void ReloadNotes(Revision revision)
    {
        if (!ReferenceEquals(revision, _revision) || _notes.Modified)
            return;
        try
        {
            _notes.Text = revision.Car.ReadNotes(revision).ReplaceLineEndings("\r\n");
            _notes.Modified = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportError?.Invoke($"The notes of {revision} can't be read: {ex.Message}");
        }
    }

    private static string Describe(Revision r)
    {
        var car = r.Car;
        var lines = new List<string>();
        var first = r.Created.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture);
        if (car.ParentOf(r) is { } parent)
            first += $" · based on {parent}";
        lines.Add(first);
        // State on one line: whether it can be edited, and why not.
        var state = new List<string>
        {
            car.LockReasonOf(r) switch
            {
                LockReason.Stock => "Locked: stock image",
                LockReason.HasChildren => "Locked: revisions are based on it",
                _ => "Editable",
            },
        };
        if (r.Rejected != null)
            state.Add($"rejected: {r.Rejected}");
        if (r.Hidden)
            state.Add("hidden");
        lines.Add(string.Join(" · ", state));
        switch (car.CheckImage(r))
        {
            case ImageState.Changed:
                lines.Add("The image was changed outside RaiderLite since it was last saved here.");
                break;
            case ImageState.Missing:
                lines.Add("The image file is missing.");
                break;
        }
        return string.Join("\n", lines);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };

    private void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ReportError?.Invoke($"{Path.GetFileName(path)} can't be opened: {ex.Message}");
        }
    }

    /// <summary>Saves the notes box if it was edited.</summary>
    public void CommitNotes()
    {
        if (_revision == null || !_notes.Modified)
            return;
        var revision = _revision;
        try
        {
            revision.Car.WriteNotes(revision, _notes.Text);
            _notes.Modified = false;
            NotesSaved?.Invoke(this, revision);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportError?.Invoke($"The notes of {revision} can't be saved: {ex.Message}");
        }
    }
}
