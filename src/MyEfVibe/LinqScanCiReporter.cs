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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static void WriteTextSummary(
        LinqScanCiSummary summary,
        string scanMode,
        string savedPath,
        LinqScanSeverity? reportMinSeverity)
    {
        var severityCounts = FormatSeverityCounts(summary, reportMinSeverity);

        AnsiConsole.MarkupLine(
            $"[bold]efvibe scan {scanMode}[/] — [yellow]{summary.TotalFindings}[/] finding(s)"
            + (severityCounts.Length > 0 ? $" ({severityCounts})" : string.Empty));

        AnsiConsole.MarkupLine($"[grey]Saved to[/] [cyan]{Markup.Escape(savedPath)}[/]");

        if (summary.FailOnThreshold is not null)
        {
            var threshold = LinqScanRuleCatalog.ToDisplayString(summary.FailOnThreshold.Value);

            if (summary.ShouldFail)
            {
                AnsiConsole.MarkupLine(
                    $"[red]CI gate failed[/] — finding(s) at or above [bold]{threshold}[/] severity.");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"[green]CI gate passed[/] — no findings at or above [bold]{threshold}[/] severity.");
            }
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
            deepStats?.SqlFailedCount,
            deepStats?.QueryPlanCount,
            deepStats?.QueryPlanFailedCount);

        Console.WriteLine(JsonSerializer.Serialize(document, JsonOptions));
    }

    private static string FormatSeverityCounts(
        LinqScanCiSummary summary,
        LinqScanSeverity? reportMinSeverity)
    {
        var parts = new List<string>();

        if (reportMinSeverity is null || reportMinSeverity <= LinqScanSeverity.Critical)
        {
            parts.Add($"[red]{summary.CriticalCount}[/] critical");
        }

        if (reportMinSeverity is null || reportMinSeverity <= LinqScanSeverity.Error)
        {
            parts.Add($"[red]{summary.ErrorCount}[/] error");
        }

        if (reportMinSeverity is null || reportMinSeverity <= LinqScanSeverity.Warning)
        {
            parts.Add($"[yellow]{summary.WarningCount}[/] warning");
        }

        if (reportMinSeverity is null || reportMinSeverity <= LinqScanSeverity.Info)
        {
            parts.Add($"[cyan]{summary.InfoCount}[/] info");
        }

        return string.Join(" · ", parts);
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
        int? SqlFailedCount = null,
        int? QueryPlanCount = null,
        int? QueryPlanFailedCount = null);
}