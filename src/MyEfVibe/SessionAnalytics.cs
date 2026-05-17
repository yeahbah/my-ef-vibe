namespace MyEfVibe;

internal sealed class SessionAnalytics
{
    private readonly List<EvaluationMetrics> _evaluations = new();
    private readonly List<object?> _exportRows = new();

    internal EvaluationMetrics? LastMetrics { get; private set; }

    internal object? LastResult { get; private set; }

    internal string? LastSnippet { get; private set; }

    internal EvaluationMetrics? CompareBaseline { get; private set; }

    internal void Record(EvaluationMetrics metrics, object? result, IReadOnlyList<object?> exportRows)
    {
        _evaluations.Add(metrics);
        LastMetrics = metrics;
        LastResult = result;
        LastSnippet = metrics.Snippet;
        _exportRows.Clear();

        foreach (var row in exportRows)
            _exportRows.Add(row);
    }

    internal void SetCompareBaseline() => CompareBaseline = LastMetrics;

    internal IReadOnlyList<EvaluationMetrics> Evaluations => _evaluations;

    internal IReadOnlyList<object?> ExportRows => _exportRows;

    internal void ClearCompareBaseline() => CompareBaseline = null;
}
