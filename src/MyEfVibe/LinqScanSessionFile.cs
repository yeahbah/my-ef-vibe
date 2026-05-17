using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe;

internal static class LinqScanSessionFile
{
    internal const int CurrentVersion = 1;
    internal const string FileName = "myefvibe-scan-lite.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string GetPath(string sessionDirectory) =>
        Path.Combine(SessionPaths.EnsureSessionDirectory(sessionDirectory), FileName);

    internal static string Save(string sessionDirectory, LinqLiteScanResult result, string displayRootDirectory)
    {
        var path = GetPath(sessionDirectory);
        var document = LinqScanSessionDocument.FromResult(result, displayRootDirectory);

        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));

        return path;
    }
}

internal sealed record LinqScanSessionDocument(
    int Version,
    DateTimeOffset ScannedAt,
    int FilesScanned,
    int ProjectsScanned,
    string DisplayRootDirectory,
    List<LinqScanFindingDto> Findings)
{
    internal static LinqScanSessionDocument FromResult(LinqLiteScanResult result, string displayRootDirectory) =>
        new(
            LinqScanSessionFile.CurrentVersion,
            DateTimeOffset.Now,
            result.FilesScanned,
            result.ProjectsScanned,
            Path.GetFullPath(displayRootDirectory.TrimEnd(Path.DirectorySeparatorChar)),
            result.Findings.Select(LinqScanFindingDto.From).ToList());

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
    string? Recommendation = null)
{
    internal static LinqScanFindingDto From(LinqScanFinding finding) =>
        new(
            finding.FilePath,
            finding.Line,
            finding.Code,
            finding.RuleId,
            finding.Message,
            finding.ResolvedRecommendation);

    internal LinqScanFinding ToFinding() =>
        new(FilePath, Line, Code, RuleId, Message, Recommendation);
}
