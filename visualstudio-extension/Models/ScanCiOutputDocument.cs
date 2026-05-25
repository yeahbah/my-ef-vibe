using System.Collections.Generic;

namespace MyEfVibe.VisualStudio.Models;

internal sealed class ScanCiOutputDocument
{
    public string? ScanMode { get; set; }
    public string? SavedPath { get; set; }
    public int TotalFindings { get; set; }
    public int InfoCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public int CriticalCount { get; set; }
    public string? FailOn { get; set; }
    public bool CiFailed { get; set; }
    public int FilesScanned { get; set; }
    public int ProjectsScanned { get; set; }
    public List<ScanFinding>? Findings { get; set; }
    public int? QuerySitesVisited { get; set; }
    public int? SqlTranslatedCount { get; set; }
    public int? SqlFailedCount { get; set; }
    public int? QueryPlanCount { get; set; }
    public int? QueryPlanFailedCount { get; set; }
}

internal sealed class ScanFinding
{
    public string? FilePath { get; set; }
    public int Line { get; set; }
    public string? Code { get; set; }
    public string? RuleId { get; set; }
    public string? Message { get; set; }
    public string? Severity { get; set; }
    public string? Recommendation { get; set; }
    public string? TranslatedSql { get; set; }
    public string? SqlTranslationNote { get; set; }
    public string? QueryPlan { get; set; }
    public string? QueryPlanNote { get; set; }
    public string? SavedNote { get; set; }
}
