using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe;

internal enum LinqScanMode
{
    Lite,
    Deep,
}

internal static class LinqScanSessionFile
{
    internal const int CurrentVersion = 2;
    internal const string LiteFileName = "myefvibe-scan-lite.json";
    internal const string DeepFileName = "myefvibe-scan-deep.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string GetPath(string sessionDirectory, LinqScanMode mode) =>
        Path.Combine(
            SessionPaths.EnsureSessionDirectory(sessionDirectory),
            mode == LinqScanMode.Deep ? DeepFileName : LiteFileName);

    internal static string Save(
        string sessionDirectory,
        LinqLiteScanResult result,
        string displayRootDirectory,
        LinqScanMode mode,
        LinqDeepScanStats? deepStats = null)
    {
        var path = GetPath(sessionDirectory, mode);
        var document = LinqScanSessionDocument.FromResult(result, displayRootDirectory, mode, deepStats);

        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));

        return path;
    }
}

internal sealed record LinqScanSessionDocument(
    int Version,
    string ScanMode,
    DateTimeOffset ScannedAt,
    int FilesScanned,
    int ProjectsScanned,
    string DisplayRootDirectory,
    List<LinqScanFindingDto> Findings,
    int? QuerySitesVisited = null,
    int? SqlTranslatedCount = null,
    int? SqlFailedCount = null)
{
    internal static LinqScanSessionDocument FromResult(
        LinqLiteScanResult result,
        string displayRootDirectory,
        LinqScanMode mode,
        LinqDeepScanStats? deepStats) =>
        new(
            LinqScanSessionFile.CurrentVersion,
            mode == LinqScanMode.Deep ? "deep" : "lite",
            DateTimeOffset.Now,
            result.FilesScanned,
            result.ProjectsScanned,
            Path.GetFullPath(displayRootDirectory.TrimEnd(Path.DirectorySeparatorChar)),
            result.Findings.Select(LinqScanFindingDto.From).ToList(),
            deepStats?.QuerySitesVisited,
            deepStats?.SqlTranslatedCount,
            deepStats?.SqlFailedCount);

    internal LinqLiteScanResult ToResult() =>
        new(
            FilesScanned,
            ProjectsScanned,
            Findings.Select(static dto => dto.ToFinding()).ToList());
}

internal sealed record LinqScanFindingDto(
    string FilePath,
    int Line,
    string Code,
    string RuleId,
    string Message,
    string? Recommendation = null,
    string? TranslatedSql = null,
    string? SqlTranslationNote = null)
{
    internal static LinqScanFindingDto From(LinqScanFinding finding) =>
        new(
            finding.FilePath,
            finding.Line,
            finding.Code,
            finding.RuleId,
            finding.Message,
            finding.ResolvedRecommendation,
            finding.TranslatedSql,
            finding.SqlTranslationNote);

    internal LinqScanFinding ToFinding() =>
        new(
            FilePath,
            Line,
            Code,
            RuleId,
            Message,
            Recommendation,
            TranslatedSql,
            SqlTranslationNote);
}
