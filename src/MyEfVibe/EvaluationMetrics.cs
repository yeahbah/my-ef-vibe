namespace MyEfVibe;

internal enum ResultKind
{
    Null,
    Scalar,
    String,
    Enumerable,
    Queryable,
    Object
}

internal sealed record EvaluationMetrics
{
    internal static EvaluationMetrics Failed(string snippet, long totalMilliseconds, string message)
    {
        return new EvaluationMetrics
        {
            Snippet = snippet,
            TotalMilliseconds = totalMilliseconds,
            Warnings = [message],
            Succeeded = false,
            ResultKind = ResultKind.Object,
            ResultTypeName = "error",
            SqlCommandCount = 0,
            IsMaterialized = false,
            ExecutedSql = []
        };
    }

    public required string Snippet { get; init; }
    public long TotalMilliseconds { get; init; }
    public long? DatabaseMilliseconds { get; init; }
    public int SqlCommandCount { get; init; }
    public string? TranslatedSql { get; init; }
    public IReadOnlyList<string> ExecutedSql { get; init; } = [];
    public ResultKind ResultKind { get; init; }
    public required string ResultTypeName { get; init; }
    public bool IsMaterialized { get; init; }
    public long? EstimatedBytes { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool Succeeded { get; init; }
    public int? RowCount { get; init; }
    public string? ConsoleOutput { get; init; }
    public int? PageIndex { get; init; }
    public int? PageSize { get; init; }
    public bool? HasMore { get; init; }
    public bool PagingSupported { get; init; }
}