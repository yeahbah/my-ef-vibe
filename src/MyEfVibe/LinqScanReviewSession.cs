namespace MyEfVibe;

internal sealed class LinqScanReviewSession
{
    private IReadOnlyList<LinqScanFinding> _findings = Array.Empty<LinqScanFinding>();
    private string _displayRoot = string.Empty;
    private string _sessionDirectory = string.Empty;
    private LinqScanMode _mode = LinqScanMode.Lite;

    internal bool IsActive { get; private set; }

    internal int Count => _findings.Count;

    internal int CurrentIndex => _index;

    private int _index;

    internal string? GetActivePrompt() =>
        IsActive ? CliUi.ScanReviewPrompt(_index + 1, Count) : null;

    internal void Begin(
        LinqLiteScanResult result,
        string sessionDirectory,
        string displayRootDirectory,
        LinqScanMode mode = LinqScanMode.Lite,
        LinqDeepScanStats? deepStats = null,
        int dismissedSkippedCount = 0)
    {
        _displayRoot = Path.GetFullPath(displayRootDirectory.TrimEnd(Path.DirectorySeparatorChar));
        _sessionDirectory = sessionDirectory;
        _mode = mode;
        var savedPath = LinqScanSessionFile.Save(sessionDirectory, result, _displayRoot, mode, deepStats);
        _findings = result.Findings;
        _index = 0;
        IsActive = _findings.Count > 0;

        if (_mode == LinqScanMode.Deep)
            LinqScanPresenter.WriteDeepSummary(result, _displayRoot, savedPath, deepStats, dismissedSkippedCount);
        else
            LinqScanPresenter.WriteLiteSummary(result, _displayRoot, savedPath, dismissedSkippedCount);

        if (_findings.Count == 0)
            return;

        LinqScanReviewPresenter.Show(_findings[_index], _index, _findings.Count, _displayRoot);
        LinqScanReviewPresenter.WriteNavigationHint(savedPath);
    }

    internal bool TrySetNote(string note)
    {
        if (!IsActive)
        {
            CliUi.WriteWarning("No scan review in progress.");
            return false;
        }

        if (_findings.Count == 0)
            return false;

        if (string.IsNullOrWhiteSpace(note))
        {
            CliUi.WriteWarning("Usage: :note <text>");
            return true;
        }

        var finding = _findings[_index];
        LinqScanNoteStore.SaveNote(_sessionDirectory, finding, note);

        var updated = finding with { SavedNote = note.Trim() };
        var list = _findings.ToList();
        list[_index] = updated;
        _findings = list;

        CliUi.WriteSuccess("Note saved — it will appear on this finding in future scans.");
        ShowCurrent();

        return true;
    }

    internal bool TryDismiss(string? note)
    {
        if (!IsActive)
        {
            CliUi.WriteWarning("No scan review in progress.");
            return false;
        }

        if (_findings.Count == 0)
            return false;

        var finding = _findings[_index];
        LinqScanDismissalStore.Dismiss(_sessionDirectory, finding, note);

        var remaining = _findings.ToList();
        remaining.RemoveAt(_index);
        _findings = remaining;

        if (_findings.Count == 0)
        {
            IsActive = false;
            CliUi.WriteSuccess("Finding dismissed. Review queue is empty.");
            return true;
        }

        if (_index >= _findings.Count)
            _index = _findings.Count - 1;

        var message = string.IsNullOrWhiteSpace(note)
            ? "Finding dismissed — it will be skipped in future scans."
            : "Finding dismissed with note — it will be skipped in future scans.";

        CliUi.WriteSuccess(message);
        ShowCurrent();

        return true;
    }

    internal bool TryNext()
    {
        if (!IsActive)
            return false;

        if (_index >= _findings.Count - 1)
        {
            CliUi.WriteWarning("Queue complete — you are at the last finding.");
            return true;
        }

        _index++;
        ShowCurrent();

        return true;
    }

    internal bool TryPrevious()
    {
        if (!IsActive)
            return false;

        if (_index <= 0)
        {
            CliUi.WriteWarning("Already at the first finding.");
            return true;
        }

        _index--;
        ShowCurrent();

        return true;
    }

    internal void GoToStart()
    {
        if (!IsActive)
        {
            CliUi.WriteWarning("No scan review in progress. Run :scan lite or :scan deep first.");
            return;
        }

        _index = 0;
        ShowCurrent();
        CliUi.WriteSuccess("Review queue restarted at the first finding.");
    }

    internal void End()
    {
        if (!IsActive)
        {
            CliUi.WriteWarning("No scan review in progress.");
            return;
        }

        IsActive = false;
        CliUi.WriteSuccess("Scan review ended.");
    }

    private void ShowCurrent() =>
        LinqScanReviewPresenter.Show(_findings[_index], _index, _findings.Count, _displayRoot);
}
