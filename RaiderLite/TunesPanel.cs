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
    private readonly RevisionCommands _commands;
    private CarSession? _session;
    private bool _splitPlaced;
    private bool _changingCar;

    public TunesPanel(MainForm main, CarRegistry cars)
    {
        _main = main;
        _cars = cars;
        _commands = new RevisionCommands(this, main);
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
        _details.NotesSaved += (_, r) => _session?.ReloadNotes(r);
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
        _session.ReloadNotes(revision);
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
            _session.ReloadNotes(revision);
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
                .Where(r => (showHidden || !r.Hidden && r.Rejected == null) && words.All(w => session.Matches(r, w)))
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
        if (item.Revision is not { } r)
        {
            return $"{item.Members.Count} revisions in side branches that are finished:\n" +
                   string.Join("\n", item.Members.Take(12).Select(m => $"{m.Number:000}  {m.Label}")) +
                   (item.Members.Count > 12 ? "\n..." : "") + "\nDouble-click to show them.";
        }
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
        if (_session?.NotesOf(r) is { } notes && notes.Trim().Length > 0)
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
            menu.Items.Add("Show in &Explorer", null, (_, _) => RevisionCommands.ShowInExplorer(File.Exists(r.ImagePath) ? r.ImagePath : r.FolderPath));
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
                menu.Items.Add("Open &Car Folder", null, (_, _) => RevisionCommands.ShowInExplorer(folder));
            }
            menu.Items.Add("Open &Library Folder", null, (_, _) =>
            {
                if (Directory.Exists(AppSettings.Current.TuneLibraryFolder))
                    RevisionCommands.ShowInExplorer(AppSettings.Current.TuneLibraryFolder);
            });
            menu.Items.Add("Choose Library &Folder...", null, (_, _) => ChooseLibraryFolder());
        }
        MainForm.ShowAndDispose(menu, anchor, location);
    }

    // ----- revision actions: done by RevisionCommands, shown here -----

    /// <summary>Runs a change of a revision's record, then shows the result.</summary>
    private void Change(Revision revision, Action change)
    {
        _commands.Change(revision, change);
        ShowChanged(revision);
    }

    /// <summary>Shows a changed revision, or the one it is based on if it is now hidden or rejected out of the timeline.</summary>
    private void ShowChanged(Revision revision)
    {
        RebuildTimeline(select: [revision.Number]);
        if (_timeline.SelectedItems.Count == 0 && revision.Parent is int parent)
            RebuildTimeline(select: [parent]);
    }

    private void DeleteRevisions(IEnumerable<Revision> revisions)
    {
        CommitEdits();
        if (!_commands.Delete(revisions, out var select))
            return;
        RebuildTimeline(select: select != null ? [select.Number] : []);
        _ = RefreshChangesAsync();
    }

    private void Rename(Revision revision)
    {
        CommitEdits();
        if (_commands.Rename(revision))
            RebuildTimeline(select: [revision.Number]);
    }

    private void Reject(Revision revision)
    {
        if (_commands.Reject(revision))
            ShowChanged(revision);
    }

    private void CopyChangelog(Revision revision)
    {
        CommitEdits();
        _commands.CopyChangelog(revision);
    }

    private void ChooseFiles(Revision revision)
    {
        if (_commands.ChooseFiles(revision))
            ShowDetails();
    }

    private void AddFiles(Revision revision, IReadOnlyList<string> paths)
    {
        _commands.AddFiles(revision, paths);
        ShowDetails();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _filterTimer.Dispose();
        base.Dispose(disposing);
    }
}
