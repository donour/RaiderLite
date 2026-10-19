using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using RaiderLite.Core.Tunes;

namespace RaiderLite;

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
                RevisionCommands.ShowInExplorer(File.Exists(_revision.ImagePath) ? _revision.ImagePath : _revision.FolderPath);
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
                menu.Items.Add("Show in E&xplorer", null, (_, _) => RevisionCommands.ShowInExplorer(path));
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
        else if (changes.Comparison is not { } comparison)
            FillChanges(null, changes.Problem ?? "The images can't be compared.");
        else if (comparison.Identical)
            FillChanges(null, "The images are identical.");
        else
            FillChanges(comparison, $"Differences from {older.Number:000}");
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
