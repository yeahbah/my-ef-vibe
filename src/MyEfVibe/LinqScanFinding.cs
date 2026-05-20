namespace MyEfVibe;

internal sealed record LinqScanFinding(
    string FilePath,
    int Line,
    string Code,
    string RuleId,
    string Message,
    LinqScanSeverity Severity,
    string? Recommendation = null,
    string? TranslatedSql = null,
    string? SqlTranslationNote = null,
    string? SavedNote = null)
{
    internal static LinqScanFinding Create(
        string filePath,
        int line,
        string code,
        string ruleId,
        string message,
        string? recommendation = null,
        string? translatedSql = null,
        string? sqlTranslationNote = null,
        string? savedNote = null) =>
        new(
            filePath,
            line,
            code,
            ruleId,
            message,
            LinqScanRuleCatalog.GetSeverity(ruleId),
            recommendation ?? LinqScanRecommendations.Get(ruleId),
            translatedSql,
            sqlTranslationNote,
            savedNote);

    internal string ResolvedRecommendation =>
        string.IsNullOrWhiteSpace(Recommendation)
            ? LinqScanRecommendations.Get(RuleId)
            : Recommendation;

    internal string GetDismissalKey()
    {
        var fullPath = Path.GetFullPath(FilePath);

        return $"{fullPath}|{Line}|{RuleId}";
    }
}

internal sealed record LinqLiteScanResult(
    int FilesScanned,
    int ProjectsScanned,
    IReadOnlyList<LinqScanFinding> Findings);
