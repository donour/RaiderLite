using RaiderLite.Core.Definitions;
using RaiderLite.Core.Model;
using RaiderLite.Core.Tunes;

namespace RaiderLite;

/// <summary>The tune manager's side of the editor window: revisions, cars and locks.</summary>
public sealed partial class MainForm
{
    // Tunes: one car object per folder, and the revision each open image is (if any).
    private readonly CarRegistry _cars = new();
    private readonly Dictionary<Rom, Revision> _romRevisions = [];

    // The bar above the table windows when the active image is a locked revision.
    private readonly Panel _lockBar = new() { Dock = DockStyle.Top, Visible = false, BackColor = Color.FromArgb(255, 244, 206) };
    private readonly Label _lockText = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false };
    private readonly Button _lockNewRevision = new() { Dock = DockStyle.Right, AutoSize = true, BackColor = SystemColors.Control };
    private Revision? _lockBarRevision;

    internal bool IsRevisionOpen(Revision revision) => _romRevisions.Values.Any(r => ReferenceEquals(r, revision));

    /// <summary>How windows name an image: "037 Race TC slip target" for a revision, else its file name.</summary>
    internal string ImageName(Rom rom) => _romRevisions.TryGetValue(rom, out var revision) ? revision.ToString() : rom.FileName;

    /// <summary>The revision an open image is, if it is one.</summary>
    internal Revision? RevisionOf(Rom? rom) => rom != null && _romRevisions.TryGetValue(rom, out var revision) ? revision : null;

    private Rom? RomOf(Revision revision) => _romRevisions.FirstOrDefault(kv => ReferenceEquals(kv.Value, revision)).Key;

    internal static string WhyLocked(Revision revision) =>
        revision.Car.LockReasonOf(revision) == LockReason.Stock ? "it is the car's stock image" : "other revisions are based on it";

    /// <summary>Makes open images of locked revisions read-only (and the others editable again).</summary>
    private void RefreshRevisionLocks()
    {
        foreach (var (rom, revision) in _romRevisions)
            rom.ReadOnlyReason = revision.Car.IsLocked(revision) ? "locked revision" : null;
        UpdateTitle();
        Toolbar.UpdateState(SelectedRom);
    }

    /// <summary>An edit of a read-only image was refused: for a locked revision, offer a new revision from it.</summary>
    internal void OnEditBlocked(Rom? rom)
    {
        if (rom?.ReadOnlyReason == null)
            return;
        if (RevisionOf(rom) is not { } revision)
        {
            MessageBox.Show(this, $"{rom.FileName} can't be changed ({rom.ReadOnlyReason}).", "Image Read-Only", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var create = new TaskDialogButton("&New Revision...");
        var page = new TaskDialogPage
        {
            Caption = "Revision Locked",
            Heading = $"{revision} is locked",
            Text = $"Its image can't change because {WhyLocked(revision)}: the revisions based on it show what " +
                   "changed from it. Make a new revision from it and change that instead.",
            Icon = TaskDialogIcon.Information,
            AllowCancel = true,
            Buttons = { create, TaskDialogButton.Cancel },
        };
        if (TaskDialog.ShowDialog(this, page) == create)
            _ = NewRevisionAsync(revision);
    }

    /// <summary>
    /// New Revision: based on <paramref name="basedOn"/> (else the current image's revision, else the
    /// one selected in the Tunes panel). Unsaved changes of the revision it is based on can be saved
    /// there first or carried into the new one. Where that revision was open, its table windows
    /// reopen on the new revision.
    /// </summary>
    internal async Task NewRevisionAsync(Revision? basedOn = null)
    {
        basedOn ??= RevisionOf(SelectedRom) ?? _tunesPanel.SelectedRevision;
        if (basedOn == null)
        {
            MessageBox.Show(this, "Open a revision, or select one in the Tunes panel, to base the new revision on.", "New Revision",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _tunesPanel.CommitEdits();
        using var dialog = new NewRevisionForm(basedOn.Car, basedOn);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var parent = dialog.BasedOn;
        var car = parent.Car;
        var parentRom = RomOf(parent);

        // The unsaved image of the parent and the rom it came from, when they go into the new revision.
        (byte[] Image, Rom From)? carried = null;
        if (parentRom is { IsDirty: true })
        {
            var save = new TaskDialogButton($"&Save them in {parent.Number:000} first");
            var carry = new TaskDialogButton("&Put them in the new revision");
            var page = new TaskDialogPage
            {
                Caption = "New Revision",
                Heading = $"{parent} has unsaved changes",
                Text = $"The new revision starts from {parent.Number:000}'s image. Its unsaved changes can go into {parent.Number:000}, or into the new revision (leaving {parent.Number:000} as it was saved).",
                Icon = TaskDialogIcon.Warning,
                AllowCancel = true,
            };
            if (!car.IsLocked(parent))
                page.Buttons.Add(save);
            page.Buttons.Add(carry);
            page.Buttons.Add(TaskDialogButton.Cancel);
            var answer = TaskDialog.ShowDialog(this, page);
            if (answer == save)
            {
                if (!SaveRom(parentRom, saveAs: false))
                    return;
            }
            else if (answer == carry)
            {
                carried = ((byte[])parentRom.PrepareForSave(out _).Clone(), parentRom);
            }
            else
            {
                return;
            }
        }

        Revision revision;
        try
        {
            revision = car.NewRevision(parent, dialog.RevisionLabel);
            if (carried is { } c)
            {
                SafeFile.WriteAllBytes(revision.ImagePath, c.Image);
                car.ImageSaved(revision);
                SuggestNotes(revision, c.From);
            }
            if (dialog.Note.Length > 0)
                car.WriteNotes(revision, dialog.Note + Environment.NewLine + car.ReadNotes(revision));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The new revision couldn't be created:\n\n{ex.Message}", "New Revision", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Carry on in the new revision where the old one was open: same tables, same places
        // (closing them stores their positions, which the new ones reopen at).
        var tables = parentRom == null ? [] : _openTables.Values.Where(f => f.Rom == parentRom).Select(f => f.Table.Name).ToList();
        if (parentRom != null)
        {
            parentRom.IsDirty = false;
            CloseRom(parentRom);
        }
        RefreshRevisionLocks();
        _tunesPanel.OnRevisionAdded(revision);
        if (await OpenRevisionAsync(revision, showTables: true) is { } rom)
        {
            foreach (var name in tables)
            {
                if (rom.GetTable(name) is { } table)
                    OpenTable(table);
            }
        }
        SetStatus($"Created {revision} in {car.Name}, based on {parent}.");
    }

    /// <summary>
    /// The first time a revision is saved with changes and no notes, starts its notes with a
    /// "* table: " line for each table it changed from its parent.
    /// </summary>
    private void SuggestNotes(Revision revision, Rom template)
    {
        var car = revision.Car;
        if (car.ParentOf(revision) is not { } parent)
            return;
        try
        {
            if (car.ReadNotes(revision).Trim().Length > 0)
                return;
            var changes = ImageDiff.Compare(template, File.ReadAllBytes(parent.ImagePath), File.ReadAllBytes(revision.ImagePath));
            if (changes.Tables.Count > 0)
                car.WriteNotes(revision, Changelog.SuggestedLines("", changes.Tables.Select(t => t.Table.Name)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Notes are a convenience; the image is saved.
        }
    }

    /// <summary>Changes a revision's label (renaming its folder) and follows the move in open images.</summary>
    internal void RelabelRevision(Revision revision, string label)
    {
        try
        {
            revision.Car.SetLabel(revision, label);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"{revision} couldn't be renamed:\n\n{ex.Message}", "Rename Revision", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        foreach (var rom in _romRevisions.Where(kv => ReferenceEquals(kv.Value, revision)).Select(kv => kv.Key).ToList())
        {
            rom.FilePath = revision.ImagePath;
            UpdateRomNode(rom);
            foreach (var form in _openTables.Values.Where(f => f.Rom == rom))
                form.Text = $"{form.Table.Name} - {ImageName(rom)}";
        }
        UpdateTitle();
    }

    /// <summary>New Car: a folder in the tune library whose revision 000 is a copy of the stock image.</summary>
    internal void NewCar()
    {
        var library = AppSettings.Current.TuneLibraryFolder;
        using var dialog = new NewCarForm(ImageFileFilter, library);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        byte[] image;
        try
        {
            image = File.ReadAllBytes(dialog.ImagePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "New Car", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        UseWaitCursor = true;
        var errors = new List<string>();
        var match = FindDefinitionMatches(AppSettings.Current.DefinitionFiles.ToList(), image, errors).FirstOrDefault();
        UseWaitCursor = false;
        if (match.Info == null)
        {
            MessageBox.Show(this, $"No definition in the definition list matches {Path.GetFileName(dialog.ImagePath)}. Add its definition with Edit > Definition Manager first." +
                (errors.Count > 0 ? "\n\n" + string.Join("\n", errors.Take(5)) : ""), "New Car", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var folder = Path.Combine(library, Car.CleanLabel(dialog.CarName));
        try
        {
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            {
                MessageBox.Show(this, $"The tune library already has a folder named {Path.GetFileName(folder)}. Choose another name.", "New Car",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Car.Create(folder, dialog.CarName, match.Info.Id.XmlId, dialog.ImagePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The car couldn't be created:\n\n{ex.Message}", "New Car", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _carsListed = true;
        ShowLeftPanel(tunes: true);
        _tunesPanel.ReloadCars(select: folder);
        SetStatus($"Created {dialog.CarName} ({match.Info.Id.XmlId}) in {library}.");
    }

    /// <summary>Closes the open images of a revision, asking about unsaved changes; false if one stays open.</summary>
    internal bool CloseRevisionImages(Revision revision)
    {
        foreach (var rom in _romRevisions.Where(kv => ReferenceEquals(kv.Value, revision)).Select(kv => kv.Key).ToList())
        {
            CloseRom(rom);
            if (_roms.Contains(rom))
                return false;
        }
        return true;
    }

    /// <summary>Revisions were deleted or re-based: locks (a parent may have lost its last child) and names change.</summary>
    internal void OnRevisionsChanged()
    {
        RefreshRevisionLocks();
        foreach (var rom in _romRevisions.Keys.ToList())
            UpdateRomNode(rom);
    }

    /// <summary>Shows the current image's revision in the Tunes panel.</summary>
    internal void ShowRevisionInTunes(Rom? rom)
    {
        if (RevisionOf(rom) is not { } revision)
            return;
        ShowLeftPanel(tunes: true);
        _tunesPanel.SelectRevision(revision);
    }

    /// <summary>The definition with this XML id among the configured definition files (also called off the UI thread).</summary>
    internal (DefinitionDocument Doc, RomDefinitionInfo Info)? FindDefinition(string xmlId)
    {
        foreach (var path in AppSettings.Current.DefinitionFiles.ToList())
        {
            if (!File.Exists(path))
                continue;
            try
            {
                var doc = LoadDefinition(path);
                if (doc.FindByXmlId(xmlId) is { } info)
                    return (doc, info);
            }
            catch (DefinitionException)
            {
                // A broken definition file can't hold it; try the next.
            }
        }
        return null;
    }

    /// <summary>
    /// Opens a revision's image (or finds it already open). With <paramref name="showTables"/> the
    /// left panel switches to its tables.
    /// </summary>
    internal async Task<Rom?> OpenRevisionAsync(Revision revision, bool showTables)
    {
        var rom = _romRevisions.FirstOrDefault(kv => ReferenceEquals(kv.Value, revision)).Key;
        if (rom == null)
        {
            if (!File.Exists(revision.ImagePath))
            {
                MessageBox.Show(this, $"The image of {revision} is missing:\n{revision.ImagePath}", "Open Revision", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            rom = await OpenImageAsync(revision.ImagePath, revision.Car.Definition);
        }
        if (rom != null && showTables)
        {
            ShowLeftPanel(tunes: false);
            if (_tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Tag == rom) is { } node)
            {
                _tree.SelectedNode = node;
                node.EnsureVisible();
            }
            _tree.Focus();
        }
        return rom;
    }

    /// <summary>Opens one table of two revisions side by side, comparing <paramref name="revision"/> against <paramref name="other"/>.</summary>
    internal async Task OpenRevisionComparisonAsync(Revision revision, Revision other, string tableName)
    {
        if (await OpenRevisionAsync(revision, showTables: false) is not { } a || await OpenRevisionAsync(other, showTables: false) is not { } b)
            return;
        var left = a.GetTable(tableName);
        var right = b.GetTable(tableName);
        if (left == null || right == null || !left.IsCompatibleWith(right))
        {
            MessageBox.Show(this, $"{tableName} can't be compared: it isn't the same table in both images.", "Compare Revisions",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        OpenComparison(left, right);
    }
}
