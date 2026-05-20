using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace MyEfVibe;

internal static class LinqScanCiReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static void WriteTextSummary(
        LinqScanCiSummary summary,
        string scanMode,
        string savedPath,
        LinqScanSeverity? minSeverity)
    {
        var minLine = minSeverity is null
            ? string.Empty
            : $" · min severity [cyan]{LinqScanRuleCatalog.ToDisplayString(minSeverity.Value)}[/]";

        AnsiConsole.MarkupLine(
            $"[bold]efvibe scan {scanMode}[/] — [yellow]{summary.TotalFindings}[/] finding(s)"
            + $" ([red]{summary.CriticalCount}[/] critical · [red]{summary.ErrorCount}[/] error · [yellow]{summary.WarningCount}[/] warning · [cyan]{summary.InfoCount}[/] info)"
            + minLine);

        AnsiConsole.MarkupLine($"[grey]Saved to[/] [cyan]{Markup.Escape(savedPath)}[/]");

        if (summary.FailOnThreshold is not null)
        {
            var threshold = LinqScanRuleCatalog.ToDisplayString(summary.FailOnThreshold.Value);

            if (summary.ShouldFail)
                AnsiConsole.MarkupLine($"[red]CI gate failed[/] — finding(s) at or above [bold]{threshold}[/] severity.");
            else
                AnsiConsole.MarkupLine($"[green]CI gate passed[/] — no findings at or above [bold]{threshold}[/] severity.");
        }
    }

    internal static void WriteJsonSummary(
        LinqLiteScanResult result,
        LinqScanCiSummary summary,
        string scanMode,
        string savedPath,
        LinqDeepScanStats? deepStats = null)
    {
        var document = new LinqScanCiOutputDocument(
            scanMode,
            savedPath,
            summary.TotalFindings,
            summary.InfoCount,
            summary.WarningCount,
            summary.ErrorCount,
            summary.CriticalCount,
            summary.FailOnThreshold is null ? null : LinqScanRuleCatalog.ToDisplayString(summary.FailOnThreshold.Value),
            summary.ShouldFail,
            result.FilesScanned,
            result.ProjectsScanned,
            result.Findings.Select(LinqScanFindingDto.From).ToList(),
            deepStats?.QuerySitesVisited,
            deepStats?.SqlTranslatedCount,
            deepStats?.SqlFailedCount);

        Console.WriteLine(JsonSerializer.Serialize(document, JsonOptions));
    }

    private sealed record LinqScanCiOutputDocument(
        string ScanMode,
        string SavedPath,
        int TotalFindings,
        int InfoCount,
        int WarningCount,
        int ErrorCount,
        int CriticalCount,
        string? FailOn,
        bool CiFailed,
        int FilesScanned,
        int ProjectsScanned,
        List<LinqScanFindingDto> Findings,
        int? QuerySitesVisited = null,
        int? SqlTranslatedCount = null,
        int? SqlFailedCount = null);
}
