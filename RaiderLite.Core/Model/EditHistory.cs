namespace RaiderLite.Core.Model;

/// <summary>
/// Undo/redo of cell edits for one ROM. Edits made inside <see cref="Begin"/> are
/// grouped into one step; edits outside a group are one step each.
/// </summary>
public sealed class EditHistory
{
    private readonly record struct Change(DataCell Cell, double Old, double New);

    private sealed class Step(string name)
    {
        public string Name { get; } = name;
        public List<Change> Changes { get; } = [];
    }

    /// <summary>Oldest steps are dropped beyond this many, so a long session can't grow without bound.</summary>
    public const int MaxSteps = 2000;

    // Used as a stack (top = last); a list so the oldest step can be dropped.
    private readonly List<Step> _undo = [];
    private readonly Stack<Step> _redo = new();
    private Step? _open;
    private int _depth;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? RedoName => _redo.TryPeek(out var s) ? s.Name : null;

    /// <summary>Raised when the undo/redo stacks change.</summary>
    public event EventHandler? Changed;

    /// <summary>Starts a group of edits that undo as one step. Dispose to end it.</summary>
    public IDisposable Begin(string name)
    {
        if (_depth++ == 0)
            _open = new Step(name);
        return new GroupScope(this);
    }

    private void End()
    {
        if (--_depth > 0)
            return;
        var step = _open;
        _open = null;
        if (step is { Changes.Count: > 0 })
            Push(step);
    }

    private void Push(Step step)
    {
        _undo.Add(step);
        if (_undo.Count > MaxSteps)
            _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Record(DataCell cell, double oldValue, double newValue)
    {
        if (_open != null)
        {
            _open.Changes.Add(new Change(cell, oldValue, newValue));
            return;
        }
        var step = new Step("Edit");
        step.Changes.Add(new Change(cell, oldValue, newValue));
        Push(step);
    }

    public void Undo()
    {
        if (_undo.Count == 0)
            return;
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        for (int i = step.Changes.Count - 1; i >= 0; i--)
            step.Changes[i].Cell.RestoreBinValue(step.Changes[i].Old);
        _redo.Push(step);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var step))
            return;
        foreach (var change in step.Changes)
            change.Cell.RestoreBinValue(change.New);
        _undo.Add(step);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class GroupScope(EditHistory history) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            history.End();
        }
    }
}
