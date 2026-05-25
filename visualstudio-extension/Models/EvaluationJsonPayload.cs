using System.Collections.Generic;

namespace MyEfVibe.VisualStudio.Models;

internal sealed class EvaluationJsonPayload
{
    public bool Success { get; set; }
    public string? Value { get; set; }
    public List<Dictionary<string, string>>? Rows { get; set; }
    public List<string>? Sql { get; set; }
    public string? TranslatedSql { get; set; }
    public string? QueryPlan { get; set; }
    public string? QueryPlanNote { get; set; }
    public EvaluationJsonMetrics? Metrics { get; set; }
    public List<string>? Warnings { get; set; }
    public string? Error { get; set; }
    public string? Snippet { get; set; }
}

internal sealed class EvaluationJsonMetrics
{
    public long TotalMs { get; set; }
    public long? DatabaseMs { get; set; }
    public int? RowCount { get; set; }
    public int SqlCommandCount { get; set; }
    public string? ResultKind { get; set; }
    public long? EstimatedBytes { get; set; }
}
