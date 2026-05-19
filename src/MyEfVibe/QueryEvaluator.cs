using System.Diagnostics;
using System.Reflection;

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

        var sqlFormatterAssemblies = host.EnumerateDiscoveryAssemblies().ToArray();

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
                    sqlFormatterAssemblies,
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

            var message = failure is TypeInitializationException or ReflectionTypeLoadException
                ? DescribeExceptionChain(failure)
                : failure.Message;

            throw new EvaluationFailedException(
                EvaluationMetrics.Failed(snippet, stopwatch.ElapsedMilliseconds, message),
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

        var formatterAssemblies = host.EnumerateDiscoveryAssemblies().ToArray();

        try
        {
            var sqlLiteral = await session.EvaluateProbeAsync(
                $"{probeExpression.TrimEnd()}.ToQueryString()",
                cancellationToken);

            if (sqlLiteral is string sqlFromScript && !string.IsNullOrWhiteSpace(sqlFromScript))
                return sqlFromScript;

            var queryable = await session.EvaluateProbeAsync(probeExpression, cancellationToken);

            return RelationalQueryableSqlFormatter.TryGetSql(
                queryable,
                formatterAssemblies,
                out var sql)
                ? sql
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string DescribeExceptionChain(Exception failure)
    {
        var lines = new List<string> { failure.Message };

        for (var inner = failure.InnerException; inner is not null; inner = inner.InnerException)
            lines.Add(inner.Message);

        return string.Join(Environment.NewLine, lines);
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
