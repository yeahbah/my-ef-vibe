namespace MyEfVibe;

internal enum ResultKind
{
    Null,
    Scalar,
    String,
    Enumerable,
    Queryable,
    Object,
}

internal sealed record EvaluationMetrics(
    string Snippet,
    long TotalMilliseconds,
    long? DatabaseMilliseconds,
    int SqlCommandCount,
    string? TranslatedSql,
    IReadOnlyList<string> ExecutedSql,
    ResultKind ResultKind,
    string ResultTypeName,
    int? RowCount,
    bool IsMaterialized,
    long? EstimatedBytes,
    IReadOnlyList<string> Warnings,
    bool Succeeded)
{
    internal static EvaluationMetrics Failed(string snippet, long totalMilliseconds, string message) =>
        new(
            snippet,
            totalMilliseconds,
            null,
            0,
            null,
            Array.Empty<string>(),
            ResultKind.Object,
            "error",
            null,
            false,
            null,
            new[] { message },
            false);
}
