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
        CancellationToken cancellationToken = default,
        QueryPagingOptions? paging = null,
        bool captureConsoleOutput = true)
    {
        var pagingSupported = QueryPagingRewriter.SupportsPaging(snippet);
        var executionSnippet = snippet;

        if (paging is { IsRequested: true })
        {
            executionSnippet = QueryPagingRewriter.TryApplyPaging(
                                   snippet,
                                   paging.Value.Skip,
                                   paging.Value.PageSize)
                               ?? snippet;
        }

        var normalizedSnippet = SnippetNormalizer.ForEvaluation(
            executionSnippet,
            session.DbContextType,
            session.PreserveAsyncQueries);
        var warnings = new List<string>(SnippetWarningsAnalyzer.Analyze(normalizedSnippet));

        if (paging is not { IsRequested: true }
            && SqlTranslationProbe.DescribeAutoMaterializationLimit(normalizedSnippet, snippet) is { } autoTakeWarning)
        {
            warnings.Add(autoTakeWarning);
        }

        using var sqlCapture = EfSqlCapture.TryAttach(dbContextInstance, dbLogSettings);
        using var consoleCapture = captureConsoleOutput
            ? new ScriptConsoleCapture()
            : null;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await session.EvaluateAsync(executionSnippet, cancellationToken);
            stopwatch.Stop();

            var (kind, typeName, rowCount, isMaterialized, estimatedBytes, exportRows) =
                ResultAnalyzer.Analyze(result);

            var executedSql = sqlCapture?.Commands.Select(EfSqlCapture.FormatEntry).ToArray()
                              ?? [];

            ExecutedSqlWarningRules.AddExecutedSqlWarnings(snippet, executedSql, warnings);

            string? translatedSql = null;

            if (executedSql.Length == 0)
            {
                translatedSql = await TryResolveTranslatedSqlFallbackAsync(
                    session,
                    executionSnippet,
                    result,
                    inspectionAssemblies,
                    dbLogSettings,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(translatedSql))
                {
                    warnings.Add(BuildTranslatedSqlWarning(result));
                }
            }

            bool? hasMore;
            int? pageIndex;
            int? pageSize;

            if (paging is { IsRequested: true })
            {
                pageIndex = paging.Value.PageIndex;
                pageSize = paging.Value.PageSize;
                hasMore = rowCount >= paging.Value.PageSize;
            }
            else if (pagingSupported && rowCount >= SqlTranslationProbe.DefaultMaterializationTake)
            {
                pageIndex = 0;
                pageSize = SqlTranslationProbe.DefaultMaterializationTake;
                hasMore = true;
            }
            else
            {
                pageIndex = null;
                pageSize = null;
                hasMore = null;
            }

            var metrics = new EvaluationMetrics
            {
                ExecutedSql = executedSql,
                TranslatedSql = translatedSql,
                ResultKind = kind,
                ResultTypeName = typeName,
                IsMaterialized = isMaterialized,
                EstimatedBytes = estimatedBytes,
                Warnings = warnings,
                Succeeded = true,   
                Snippet = normalizedSnippet,
                TotalMilliseconds = stopwatch.ElapsedMilliseconds,
                DatabaseMilliseconds = sqlCapture is { HasEntries: true } ? sqlCapture.TotalDatabaseMilliseconds : null,
                SqlCommandCount = sqlCapture?.Commands.Count ?? 0,
                RowCount = rowCount,
                ConsoleOutput = consoleCapture?.CapturedOutput,
                PageIndex = pageIndex,
                PageSize = pageSize,
                HasMore = hasMore,
                PagingSupported = pagingSupported,
            };

            return (result, metrics);
        }
        catch (Exception failure)
        {
            stopwatch.Stop();
            var message = failure is TypeInitializationException or ReflectionTypeLoadException
                ? DescribeExceptionChain(failure)
                : DescribeExceptionChain(UnwrapEvaluationException(failure));

            throw new EvaluationFailedException(
                EvaluationMetrics.Failed(snippet, stopwatch.ElapsedMilliseconds, message),
                failure);
        }
    }

    private static async Task<string?> TryResolveTranslatedSqlFallbackAsync(
        ScriptSession session,
        string snippet,
        object? result,
        IEnumerable<Assembly> inspectionAssemblies,
        DbLogSettings dbLogSettings,
        CancellationToken cancellationToken)
    {
        if (SqlTranslationProbe.TryCreateProbeExpression(snippet) is { } probeExpression)
        {
            try
            {
                if (SqlTranslationProbe.LooksLikeAggregateTerminalProbe(probeExpression))
                {
                    using var sqlCapture = EfSqlCapture.TryAttach(session.DbContext, dbLogSettings);

                    if (sqlCapture is not null)
                    {
                        await session.EvaluateProbeAsync(
                            ProbeScriptFormatter.ToScriptExpression(probeExpression),
                            cancellationToken);

                        var capturedSql = sqlCapture.Commands
                            .Select(EfSqlCapture.FormatEntry)
                            .LastOrDefault(static entry => !string.IsNullOrWhiteSpace(entry));

                        if (!string.IsNullOrWhiteSpace(capturedSql))
                        {
                            return capturedSql;
                        }
                    }
                }
                else
                {
                    var probeSql = await session.EvaluateProbeAsync(
                        ProbeScriptFormatter.ToQueryStringProbe(probeExpression),
                        cancellationToken);

                    if (probeSql is string literal && !string.IsNullOrWhiteSpace(literal))
                    {
                        return literal;
                    }
                }
            }
            catch
            {
                // Fall back to the evaluated result when the probe cannot be translated.
            }
        }

        if (result is IQueryable
            && RelationalQueryableSqlFormatter.TryGetSql(result, inspectionAssemblies, out var queryString))
        {
            return queryString;
        }

        return null;
    }

    private static string BuildTranslatedSqlWarning(object? result)
    {
        return result is IQueryable
            ? "Query not executed; showing translated SQL from ToQueryString()."
            : "Executed SQL was not captured from the database log; showing translated SQL from ToQueryString() (provider LIMIT/TOP may differ at runtime).";
    }

    private static Exception UnwrapEvaluationException(Exception failure)
    {
        while (true)
        {
            switch (failure)
            {
                case TargetInvocationException { InnerException: { } inner }:
                    failure = inner;
                    continue;
                case AggregateException { InnerExceptions.Count: 1 } aggregate:
                    failure = aggregate.InnerExceptions[0];
                    continue;
                default:
                    return failure;
            }
        }
    }

    private static string DescribeExceptionChain(Exception failure)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = failure; current is not null; current = current.InnerException)
        {
            var line = string.IsNullOrWhiteSpace(current.Message)
                       || string.Equals(
                           current.Message,
                           "Exception has been thrown by the target of an invocation.",
                           StringComparison.Ordinal)
                ? $"{current.GetType().Name}"
                : $"{current.GetType().Name}: {current.Message}";

            if (!seen.Add(line))
            {
                continue;
            }

            lines.Add(line);
        }

        return lines.Count == 0 ? failure.Message : string.Join(Environment.NewLine, lines);
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