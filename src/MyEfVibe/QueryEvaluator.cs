using System.Diagnostics;
using System.Reflection;

namespace MyEfVibe;

internal static class QueryEvaluator
{
    internal static async Task<(object? Result, EvaluationMetrics Metrics)> EvaluateAsync(
        object dbContextInstance,
        ScriptSession session,
        string snippet,
        DbLogSettings dbLogSettings,
        IEnumerable<Assembly> inspectionAssemblies,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>(SnippetWarningsAnalyzer.Analyze(snippet));

        using var sqlCapture = EfSqlCapture.TryAttach(dbContextInstance, dbLogSettings);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await session.EvaluateAsync(snippet, cancellationToken);

            stopwatch.Stop();

            var (kind, typeName, rowCount, isMaterialized, estimatedBytes, exportRows) =
                ResultAnalyzer.Analyze(result);

            var executedSql = sqlCapture?.Commands.Select(EfSqlCapture.FormatEntry).ToArray()
                ?? Array.Empty<string>();

            string? translatedSql = null;

            if (executedSql.Length == 0
                && result is System.Linq.IQueryable
                && RelationalQueryableSqlFormatter.TryGetSql(result, inspectionAssemblies, out var queryString))
            {
                translatedSql = queryString;
                warnings.Add("Query not executed; showing translated SQL from ToQueryString().");
            }

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
