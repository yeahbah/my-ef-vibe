namespace MyEfVibe;

internal sealed class LinqScanReviewSession
{
    private IReadOnlyList<LinqScanFinding> _findings = Array.Empty<LinqScanFinding>();
    private string _displayRoot = string.Empty;
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
        LinqDeepScanStats? deepStats = null)
    {
        _displayRoot = Path.GetFullPath(displayRootDirectory.TrimEnd(Path.DirectorySeparatorChar));
        _mode = mode;
        var savedPath = LinqScanSessionFile.Save(sessionDirectory, result, _displayRoot, mode, deepStats);
        _findings = result.Findings;
        _index = 0;
        IsActive = true;

        if (_mode == LinqScanMode.Deep)
            LinqScanPresenter.WriteDeepSummary(result, _displayRoot, savedPath, deepStats);
        else
            LinqScanPresenter.WriteLiteSummary(result, _displayRoot, savedPath);

        if (_findings.Count == 0)
            return;

        LinqScanReviewPresenter.Show(_findings[_index], _index, _findings.Count, _displayRoot);
        LinqScanReviewPresenter.WriteNavigationHint(savedPath);
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
