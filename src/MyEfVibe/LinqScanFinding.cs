namespace MyEfVibe;

internal sealed record LinqScanFinding(
    string FilePath,
    int Line,
    string Code,
    string RuleId,
    string Message,
    string? Recommendation = null,
    string? TranslatedSql = null,
    string? SqlTranslationNote = null,
    string? SavedNote = null)
{
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
