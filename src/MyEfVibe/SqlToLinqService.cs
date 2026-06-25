using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal static class SqlToLinqService
{
    internal static async Task<SqlToLinqConverter.SqlToLinqDraft> ConvertAndValidateAsync(
        object dbContext,
        ScriptSession session,
        IEnumerable<Assembly> inspectionAssemblies,
        DbLogSettings dbLogSettings,
        string sql,
        CancellationToken cancellationToken = default)
    {
        var draft = SqlToLinqConverter.Convert(dbContext, sql);

        if (string.IsNullOrWhiteSpace(draft.Linq) || draft.Linq.StartsWith("//", StringComparison.Ordinal))
        {
            return draft;
        }

        var probe = SqlTranslationProbe.TryCreateProbeExpression(draft.Linq);

        if (string.IsNullOrWhiteSpace(probe))
        {
            return draft;
        }

        var expression = probe.Contains("ToQueryString", StringComparison.Ordinal)
            ? probe
            : $"{probe}.ToQueryString()";

        try
        {
            var (_, metrics) = await QueryEvaluator.EvaluateAsync(
                dbContext,
                session,
                expression,
                dbLogSettings,
                inspectionAssemblies,
                cancellationToken);

            draft.TranslatedSql = metrics.TranslatedSql ?? metrics.ExecutedSql.FirstOrDefault();
            draft.Similarity = SqlSimilarity.Compare(sql, draft.TranslatedSql);
        }
        catch
        {
            // Validation is best-effort; keep the draft.
        }

        return draft;
    }
}

internal static class SqlToLinqJsonReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    internal static void Write(SqlToLinqConverter.SqlToLinqDraft draft)
    {
        var payload = new SqlToLinqJsonPayload
        {
            Linq = draft.Linq,
            Confidence = draft.Confidence,
            Unsupported = draft.Unsupported.ToArray(),
            Mappings = draft.Mappings
                .Select(mapping => new SqlToLinqJsonMapping
                {
                    Table = mapping.Table,
                    DbSet = mapping.DbSet,
                    Entity = mapping.Entity
                })
                .ToArray(),
            TranslatedSql = draft.TranslatedSql,
            Similarity = draft.Similarity
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private sealed class SqlToLinqJsonPayload
    {
        public string Linq { get; init; } = string.Empty;

        public string Confidence { get; init; } = "low";

        public string[] Unsupported { get; init; } = [];

        public SqlToLinqJsonMapping[] Mappings { get; init; } = [];

        public string? TranslatedSql { get; init; }

        public double? Similarity { get; init; }
    }

    private sealed class SqlToLinqJsonMapping
    {
        public string Table { get; init; } = string.Empty;

        public string DbSet { get; init; } = string.Empty;

        public string Entity { get; init; } = string.Empty;
    }
}
