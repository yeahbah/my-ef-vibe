namespace MyEfVibe;

internal sealed record LinqDeepScanStats(
    int QuerySitesVisited,
    int SqlTranslatedCount,
    int SqlFailedCount);

internal static class LinqDeepScanner
{
    internal static async Task<(LinqLiteScanResult Result, LinqDeepScanStats Stats)> ScanAsync(
        string efProjectPath,
        string startupProjectPath,
        ScriptSession session,
        WorkspaceHost host,
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var lite = LinqLiteScanner.Scan(efProjectPath, startupProjectPath);
        var sites = LinqQuerySiteCollector.Collect(efProjectPath, startupProjectPath);

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
                cancellationToken);

            sqlBySite[siteKey] = translation;

            completed++;
            progress?.Report((completed, uniqueSites.Length));
        }

        var translatedCount = sqlBySite.Values.Count(static result => !string.IsNullOrWhiteSpace(result.Sql));
        var failedCount = uniqueSites.Length - translatedCount;

        var warningLines = lite.Findings
            .Select(static finding => $"{finding.FilePath}|{finding.Line}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var findings = new List<LinqScanFinding>(lite.Findings.Count + uniqueSites.Length);

        foreach (var finding in lite.Findings)
        {
            var siteKey = $"{finding.FilePath}|{finding.Line}";
            sqlBySite.TryGetValue(siteKey, out var translation);

            findings.Add(finding with
            {
                TranslatedSql = translation?.Sql,
                SqlTranslationNote = translation?.Note,
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

            var message = !string.IsNullOrWhiteSpace(translation.Sql)
                ? "Queryable call site — translated SQL available."
                : "Queryable call site — SQL translation failed.";

            findings.Add(LinqScanFinding.Create(
                site.FilePath,
                site.Line,
                site.Code,
                "query-site",
                message,
                translatedSql: translation.Sql,
                sqlTranslationNote: translation.Note));
        }

        var ordered = findings
            .OrderByDescending(static finding => finding.Severity)
            .ThenBy(static finding => finding.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static finding => finding.Line)
            .ThenBy(static finding => finding.RuleId, StringComparer.Ordinal)
            .ToArray();

        var result = new LinqLiteScanResult(lite.FilesScanned, lite.ProjectsScanned, ordered);
        var stats = new LinqDeepScanStats(uniqueSites.Length, translatedCount, failedCount);

        return (result, stats);
    }
}
