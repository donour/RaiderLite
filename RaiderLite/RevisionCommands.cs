using System.ComponentModel;
using System.Diagnostics;
using RaiderLite.Core.Tunes;

namespace RaiderLite;

/// <summary>
/// What can be done to revisions from the Tunes panel, with the confirmations, prompts and error
/// messages that go with it. Each command reports whether it changed anything; showing the
/// result is up to the caller.
/// </summary>
internal sealed class RevisionCommands(IWin32Window owner, MainForm main)
{
    /// <summary>Runs a change of a revision's record, reporting a failure.</summary>
    public void Change(Revision revision, Action change)
    {
        try
        {
            change();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(owner, ex.Message, revision.ToString(), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Deletes revisions after confirming: their folders go to the Recycle Bin and the revisions
    /// based on them are based on the nearest surviving ancestor instead. 000 is never deleted.
    /// Returns false if nothing was deleted because the user cancelled; otherwise
    /// <paramref name="select"/> is the revision to show instead (the last one's survivor).
    /// </summary>
    public bool Delete(IEnumerable<Revision> selection, out Revision? select)
    {
        select = null;
        // Newest first, so a deleted revision's children are re-based before it goes.
        var revisions = selection.Where(r => r.Number != 0).OrderByDescending(r => r.Number).ToList();
        if (revisions.Count == 0)
            return false;
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
        if (TaskDialog.ShowDialog(owner, page) != delete)
            return false;

        select = Survivor(revisions[^1]);
        int done = 0;
        foreach (var revision in revisions)
        {
            if (!main.CloseRevisionImages(revision))
                break;
            try
            {
                car.DeleteRevision(revision, path =>
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin));
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                MessageBox.Show(owner, $"{revision} couldn't be deleted:\n\n{ex.Message}", "Delete Revision", MessageBoxButtons.OK, MessageBoxIcon.Error);
                break;
            }
        }
        main.OnRevisionsChanged();
        main.SetStatus(done == 1 ? $"Deleted {revisions[0]} (its folder is in the Recycle Bin)." : $"Deleted {done} revisions (their folders are in the Recycle Bin).");
        return true;
    }

    /// <summary>Asks for a new label (the folder is renamed to match). False if it wasn't changed.</summary>
    public bool Rename(Revision revision)
    {
        using var prompt = new TextPromptForm("Rename Revision", $"New label for {revision.Number:000} (its folder is renamed to match):", revision.Label) { HelpTopic = "tunes#revision-commands" };
        if (prompt.ShowDialog(owner) != DialogResult.OK || prompt.Value == revision.Label)
            return false;
        main.RelabelRevision(revision, prompt.Value);
        return true;
    }

    /// <summary>Asks why the revision is rejected and marks it so. False if the user cancelled.</summary>
    public bool Reject(Revision revision)
    {
        using var prompt = new TextPromptForm("Reject Revision", $"Why is {revision} rejected? (e.g. too lean)", "") { HelpTopic = "tunes#revision-commands" };
        if (prompt.ShowDialog(owner) != DialogResult.OK)
            return false;
        Change(revision, () => revision.Car.SetRejected(revision, prompt.Value.Length > 0 ? prompt.Value : "rejected"));
        if (!AppSettings.Current.ShowHiddenRevisions)
            main.SetStatus($"{revision} is rejected and hidden from the timeline (⋯ > Show Hidden and Rejected Revisions).");
        return true;
    }

    /// <summary>Copies the notes of the revision and the ones it descends from, as a changelog.</summary>
    public void CopyChangelog(Revision revision)
    {
        try
        {
            Clipboard.SetText(Changelog.Build(revision.Car, revision, CarSession.ReadNotes));
            main.SetStatus($"Copied the changelog of {revision} ({revision.Car.Lineage(revision).Count} revisions) to the clipboard.");
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            main.SetStatus($"The clipboard is in use: {ex.Message}");
        }
    }

    /// <summary>Asks for files to add to the revision's folder. False if the user cancelled.</summary>
    public bool ChooseFiles(Revision revision)
    {
        using var dialog = new OpenFileDialog { Title = $"Add Files to {revision}", Multiselect = true, Filter = "All files (*.*)|*.*" };
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return false;
        AddFiles(revision, dialog.FileNames);
        return true;
    }

    /// <summary>Copies files (logs, flash files...) into the revision's folder.</summary>
    public void AddFiles(Revision revision, IReadOnlyList<string> paths)
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
                MessageBox.Show(owner, $"{Path.GetFileName(path)} couldn't be added:\n\n{ex.Message}", "Add Files", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        main.SetStatus($"Added {added} file(s) to {revision}.");
    }

    /// <summary>Opens Explorer on a folder, or on a file's folder with the file selected.</summary>
    public static void ShowInExplorer(string path)
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
}
