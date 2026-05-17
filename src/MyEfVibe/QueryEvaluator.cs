using System.Diagnostics;

namespace MyEfVibe;

internal static class QueryEvaluator
{
    internal static async Task<(object? Result, EvaluationMetrics Metrics)> EvaluateAsync(
        object dbContextInstance,
        ScriptSession session,
        string snippet,
        SqlDisplaySettings sqlSettings,
        WorkspaceHost host,
        CancellationToken cancellationToken = default)
    {
        var warnings = SnippetWarningsAnalyzer.Analyze(snippet);
        string? translatedSql = null;

        if (sqlSettings.ShowSql)
            translatedSql = await TryGetProbeTranslatedSqlAsync(session, snippet, host, cancellationToken);

        using var sqlCapture = EfSqlCapture.TryAttach(dbContextInstance);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await session.EvaluateAsync(snippet, cancellationToken);

            stopwatch.Stop();

            if (translatedSql is null && sqlSettings.ShowSql)
            {
                RelationalQueryableSqlFormatter.TryGetSql(
                    result,
                    host.EnumerateLoadedAssemblies(),
                    out translatedSql);
            }

            var (kind, typeName, rowCount, isMaterialized, estimatedBytes, exportRows) =
                ResultAnalyzer.Analyze(result);

            var executedSql = sqlCapture?.Commands.Select(EfSqlCapture.FormatEntry).ToArray()
                ?? Array.Empty<string>();

            var metrics = new EvaluationMetrics(
                snippet,
                stopwatch.ElapsedMilliseconds,
                sqlCapture is { HasEntries: true } ? sqlCapture.TotalDatabaseMilliseconds : null,
                sqlCapture?.Commands.Count ?? 0,
                translatedSql,
                executedSql,
                kind,
                typeName,
                rowCount,
                isMaterialized,
                estimatedBytes,
                warnings,
                true);

            return (result, metrics);
        }
        catch (Exception failure)
        {
            stopwatch.Stop();

            throw new EvaluationFailedException(
                EvaluationMetrics.Failed(snippet, stopwatch.ElapsedMilliseconds, failure.Message),
                failure);
        }
    }

    private static async Task<string?> TryGetProbeTranslatedSqlAsync(
        ScriptSession session,
        string snippet,
        WorkspaceHost host,
        CancellationToken cancellationToken)
    {
        var probeExpression = SqlTranslationProbe.TryCreateProbeExpression(snippet);

        if (probeExpression is null)
            return null;

        try
        {
            var queryable = await session.EvaluateAsync(probeExpression, cancellationToken);

            return RelationalQueryableSqlFormatter.TryGetSql(
                queryable,
                host.EnumerateLoadedAssemblies(),
                out var sql)
                ? sql
                : null;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class EvaluationFailedException : Exception
{
    internal EvaluationFailedException(EvaluationMetrics metrics, Exception inner)
        : base(inner.Message, inner)
    {
        Metrics = metrics;
    }

    internal EvaluationMetrics Metrics { get; }
}
