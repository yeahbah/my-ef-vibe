namespace MyEfVibe;

internal sealed class InputHistory
{
    private readonly List<string> _entries = new();
    private int _navigationIndex;

    internal int Count => _entries.Count;

    internal IReadOnlyList<string> Entries => _entries;

    internal void Add(string entry)
    {
        var trimmed = entry.Trim();

        if (string.IsNullOrEmpty(trimmed))
            return;

        if (_entries.Count > 0 && string.Equals(_entries[^1], trimmed, StringComparison.Ordinal))
            return;

        _entries.Add(trimmed);
        _navigationIndex = _entries.Count;
    }

    internal void ResetNavigation() => _navigationIndex = _entries.Count;

    internal bool TryNavigateUp(out string entry)
    {
        entry = string.Empty;

        if (_entries.Count == 0)
            return false;

        if (_navigationIndex > 0)
            _navigationIndex--;

        entry = _entries[_navigationIndex];

        return true;
    }

    internal bool TryNavigateDown(out string entry)
    {
        entry = string.Empty;

        if (_entries.Count == 0)
            return false;

        if (_navigationIndex >= _entries.Count - 1)
        {
            _navigationIndex = _entries.Count;

            return false;
        }

        _navigationIndex++;

        entry = _entries[_navigationIndex];

        return true;
    }
}
