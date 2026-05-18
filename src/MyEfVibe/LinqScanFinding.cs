namespace MyEfVibe;

internal sealed record LinqScanFinding(
    string FilePath,
    int Line,
    string Code,
    string RuleId,
    string Message,
    string? Recommendation = null,
    string? TranslatedSql = null,
    string? SqlTranslationNote = null)
{
    internal string ResolvedRecommendation =>
        string.IsNullOrWhiteSpace(Recommendation)
            ? LinqScanRecommendations.Get(RuleId)
            : Recommendation;
}

internal sealed record LinqLiteScanResult(
    int FilesScanned,
    int ProjectsScanned,
    IReadOnlyList<LinqScanFinding> Findings);
