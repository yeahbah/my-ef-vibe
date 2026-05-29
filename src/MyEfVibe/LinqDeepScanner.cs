namespace MyEfVibe;

internal sealed record LinqDeepScanStats(
    int QuerySitesVisited,
    int SqlTranslatedCount,
    int SqlFailedCount,
    int QueryPlanCount,
    int QueryPlanFailedCount);

internal static class LinqDeepScanner
{
    internal static async Task<(LinqLiteScanResult Result, LinqDeepScanStats Stats)> ScanAsync(
        string efProjectPath,
        string startupProjectPath,
        ScriptSession session,
        WorkspaceHost host,
        Type selectedDbContextType,
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var lite = LinqLiteScanner.Scan(efProjectPath, startupProjectPath, selectedDbContextType);
        var sites = LinqQuerySiteCollector.Collect(efProjectPath, startupProjectPath, selectedDbContextType);

        var uniqueSites = sites
            .GroupBy(static site => $"{site.FilePath}|{site.Line}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        var sqlBySite = new Dictionary<string, LinqSqlTranslationResult>(
            uniqueSites.Length,
            StringComparer.OrdinalIgnoreCase);

        var completed = 0;

        foreach (var site in uniqueSites)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var siteKey = $"{site.FilePath}|{site.Line}";

            var translation = await LinqDeepSqlTranslator.TranslateAsync(
                session,
                host,
                site.Statement,
                site.ContextInstanceIdentifiers,
                cancellationToken);

            sqlBySite[siteKey] = translation;

            completed++;
            progress?.Report((completed, uniqueSites.Length));
        }

        var translatedCount = sqlBySite.Values.Count(static result => !string.IsNullOrWhiteSpace(result.Sql));
        var failedCount = uniqueSites.Length - translatedCount;
        var planCount = sqlBySite.Values.Count(static result => !string.IsNullOrWhiteSpace(result.QueryPlan));
        var planFailedCount = sqlBySite.Values.Count(static result =>
            !string.IsNullOrWhiteSpace(result.Sql)
            && string.IsNullOrWhiteSpace(result.QueryPlan)
            && !string.IsNullOrWhiteSpace(result.QueryPlanNote));

        var warningLines = lite.Findings
            .Select(static finding => $"{finding.FilePath}|{finding.Line}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var findings = new List<LinqScanFinding>(lite.Findings.Count + uniqueSites.Length);

        foreach (var finding in lite.Findings)
        {
            var siteKey = $"{finding.FilePath}|{finding.Line}";
            sqlBySite.TryGetValue(siteKey, out var translation);

            if (TryCreateDeepScanReplacementFinding(finding, translation, out var replacement))
            {
                findings.Add(replacement);
                continue;
            }

            findings.Add(finding with
            {
                TranslatedSql = translation?.Sql,
                SqlTranslationNote = translation?.Note,
                QueryPlan = translation?.QueryPlan,
                QueryPlanNote = translation?.QueryPlanNote,
            });
        }

        foreach (var site in uniqueSites)
        {
            var siteKey = $"{site.FilePath}|{site.Line}";

            if (warningLines.Contains(siteKey))
                continue;

            if (!sqlBySite.TryGetValue(siteKey, out var translation))
                continue;

            if (string.IsNullOrWhiteSpace(translation.Sql) && string.IsNullOrWhiteSpace(translation.Note))
                continue;

            var (ruleId, message) = ResolveQuerySiteRule(translation);

            findings.Add(LinqScanFinding.Create(
                site.FilePath,
                site.Line,
                site.Code,
                ruleId,
                message,
                translatedSql: translation.Sql,
                sqlTranslationNote: translation.Note,
                queryPlan: translation.QueryPlan,
                queryPlanNote: translation.QueryPlanNote));
        }

        var ordered = findings
            .OrderByDescending(static finding => finding.Severity)
            .ThenBy(static finding => finding.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static finding => finding.Line)
            .ThenBy(static finding => finding.RuleId, StringComparer.Ordinal)
            .ToArray();

        var result = new LinqLiteScanResult(lite.FilesScanned, lite.ProjectsScanned, ordered);
        var stats = new LinqDeepScanStats(
            uniqueSites.Length,
            translatedCount,
            failedCount,
            planCount,
            planFailedCount);

        return (result, stats);
    }

    private static bool TryCreateDeepScanReplacementFinding(
        LinqScanFinding finding,
        LinqSqlTranslationResult? translation,
        out LinqScanFinding replacement)
    {
        replacement = finding;

        if (LinqSqlTranslationNotes.ShouldReplaceUnboundedMaterializeWithUnmappedEntity(
                finding.RuleId,
                translation?.Note))
        {
            replacement = LinqScanFinding.Create(
                finding.FilePath,
                finding.Line,
                finding.Code,
                "unmapped-entity",
                "Query targets an entity type that is not mapped for the current DbContext configuration.",
                translatedSql: translation?.Sql,
                sqlTranslationNote: translation?.Note,
                queryPlan: translation?.QueryPlan,
                queryPlanNote: translation?.QueryPlanNote);
            return true;
        }

        if (LinqSqlTranslationNotes.ShouldReplaceCartesianWithInvalidInclude(
                finding.RuleId,
                translation?.Note))
        {
            replacement = LinqScanFinding.Create(
                finding.FilePath,
                finding.Line,
                finding.Code,
                "invalid-navigation-include",
                "Include path cannot be translated — navigation may be ignored or misconfigured for this provider.",
                translatedSql: translation?.Sql,
                sqlTranslationNote: translation?.Note,
                queryPlan: translation?.QueryPlan,
                queryPlanNote: translation?.QueryPlanNote);
            return true;
        }

        return false;
    }

    private static (string RuleId, string Message) ResolveQuerySiteRule(LinqSqlTranslationResult translation)
    {
        if (LinqSqlTranslationNotes.IsEntityNotIncludedInModelNote(translation.Note))
        {
            return (
                "unmapped-entity",
                "Query targets an entity type that is not mapped for the current DbContext configuration.");
        }

        if (LinqSqlTranslationNotes.IsInvalidIncludeTranslationNote(translation.Note))
        {
            return (
                "invalid-navigation-include",
                "Include path cannot be translated — navigation may be ignored or misconfigured for this provider.");
        }

        if (!string.IsNullOrWhiteSpace(translation.Sql))
            return ("query-site", "Queryable call site — translated SQL available.");

        return ("query-site", "Queryable call site — SQL translation failed.");
    }
}
