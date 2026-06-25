using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe.Reporters;

internal enum CliOutputFormat
{
    Text,
    Json
}

internal static class EvaluationJsonReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    internal static void WriteSuccess(object? result, EvaluationMetrics metrics, QueryPlanResult? plan = null)
    {
        var payload = BuildSuccess(result, metrics, plan);
        Console.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    internal static void WriteFailure(EvaluationMetrics metrics, string? error = null)
    {
        var payload = BuildFailure(metrics, error);
        Console.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    internal static void WriteSqlSuccess(
        object? result,
        IReadOnlyList<Dictionary<string, string>>? rows,
        EvaluationMetrics metrics,
        QueryPlanResult? plan = null)
    {
        var payload = new EvaluationJsonPayload
        {
            Success = true,
            Value = FormatSqlValue(result, rows),
            Rows = rows is { Count: > 0 } ? rows : null,
            Sql = BuildSql(metrics),
            TranslatedSql = metrics.TranslatedSql,
            QueryPlan = string.IsNullOrWhiteSpace(plan?.PlanText) ? null : plan.PlanText,
            QueryPlanNote = string.IsNullOrWhiteSpace(plan?.PlanText) ? plan?.Note : null,
            Metrics = EvaluationJsonMetrics.From(metrics),
            Warnings = metrics.Warnings,
            Snippet = metrics.Snippet
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private static string? FormatSqlValue(
        object? result,
        IReadOnlyList<Dictionary<string, string>>? rows)
    {
        if (result is string text)
        {
            return text;
        }

        if (rows is { Count: > 0 })
        {
            return rows.Count == 1 ? FormatSingleRowSummary(rows[0]) : $"{rows.Count} rows";
        }

        return result?.ToString();
    }

    private static string FormatSingleRowSummary(IReadOnlyDictionary<string, string> row)
    {
        if (row.Count == 1)
        {
            return row.Values.First();
        }

        return string.Join(", ", row.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    internal static EvaluationJsonPayload BuildSuccess(
        object? result,
        EvaluationMetrics metrics,
        QueryPlanResult? plan = null)
    {
        var (_, _, _, _, _, exportRows) = ResultAnalyzer.Analyze(result);
        var sql = BuildSql(metrics);

        return new EvaluationJsonPayload
        {
            Success = true,
            Value = FormatValue(result, exportRows),
            Rows = BuildRows(exportRows),
            Sql = sql,
            TranslatedSql = metrics.TranslatedSql,
            QueryPlan = string.IsNullOrWhiteSpace(plan?.PlanText) ? null : plan.PlanText,
            QueryPlanNote = string.IsNullOrWhiteSpace(plan?.PlanText) ? plan?.Note : null,
            Metrics = EvaluationJsonMetrics.From(metrics),
            Warnings = metrics.Warnings,
            Snippet = metrics.Snippet
        };
    }

    internal static EvaluationJsonPayload BuildFailure(EvaluationMetrics metrics, string? error)
    {
        var message = error
                      ?? metrics.Warnings.FirstOrDefault()
                      ?? "Evaluation failed.";

        return new EvaluationJsonPayload
        {
            Success = false,
            Error = message,
            Sql = BuildSql(metrics),
            TranslatedSql = metrics.TranslatedSql,
            Metrics = EvaluationJsonMetrics.From(metrics),
            Warnings = metrics.Warnings,
            Snippet = metrics.Snippet
        };
    }

    private static IReadOnlyList<string> BuildSql(EvaluationMetrics metrics)
    {
        if (metrics.ExecutedSql.Count > 0)
        {
            return metrics.ExecutedSql;
        }

        return string.IsNullOrWhiteSpace(metrics.TranslatedSql)
            ? Array.Empty<string>()
            : new[] { metrics.TranslatedSql };
    }

    private static string? FormatValue(object? result, IReadOnlyList<object?> exportRows)
    {
        if (result is null)
        {
            return null;
        }

        if (result is string text)
        {
            return text;
        }

        if (result is IQueryable)
        {
            return null;
        }

        if (result is IEnumerable and not string)
        {
            if (exportRows.Count == 0)
            {
                return "(empty)";
            }

            if (exportRows.Count == 1)
            {
                return exportRows[0]?.ToString();
            }

            return $"{exportRows.Count} rows";
        }

        return result.ToString();
    }

    private static IReadOnlyList<Dictionary<string, string>>? BuildRows(IReadOnlyList<object?> exportRows)
    {
        if (exportRows.Count == 0)
        {
            return null;
        }

        var json = TabularExportBuilder.ToJson(exportRows);

        return JsonSerializer.Deserialize<IReadOnlyList<Dictionary<string, string>>>(json);
    }

    internal sealed class EvaluationJsonPayload
    {
        public bool Success { get; init; }

        public string? Value { get; init; }

        public IReadOnlyList<Dictionary<string, string>>? Rows { get; init; }

        public IReadOnlyList<string> Sql { get; init; } = [];

        public string? TranslatedSql { get; init; }

        public string? QueryPlan { get; init; }

        public string? QueryPlanNote { get; init; }

        public EvaluationJsonMetrics Metrics { get; init; } = new();

        public IReadOnlyList<string> Warnings { get; init; } = [];

        public string? Error { get; init; }

        public string? Snippet { get; init; }
    }

    internal sealed class EvaluationJsonMetrics
    {
        public long TotalMs { get; init; }

        public long? DatabaseMs { get; init; }

        public int? RowCount { get; init; }

        public int SqlCommandCount { get; init; }

        public string ResultKind { get; init; } = string.Empty;

        public long? EstimatedBytes { get; init; }

        public static EvaluationJsonMetrics From(EvaluationMetrics metrics)
        {
            return new EvaluationJsonMetrics
            {
                TotalMs = metrics.TotalMilliseconds,
                DatabaseMs = metrics.DatabaseMilliseconds,
                RowCount = metrics.RowCount,
                SqlCommandCount = metrics.SqlCommandCount,
                ResultKind = metrics.ResultKind.ToString(),
                EstimatedBytes = metrics.EstimatedBytes
            };
        }
    }
}