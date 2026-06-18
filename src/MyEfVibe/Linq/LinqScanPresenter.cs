using Spectre.Console;

namespace MyEfVibe.Linq;

internal static class LinqScanPresenter
{
    internal static void WriteLiteSummary(
        LinqLiteScanResult result,
        string displayRootDirectory,
        string savedPath,
        int dismissedSkippedCount = 0)
    {
        if (result.Findings.Count == 0)
        {
            if (dismissedSkippedCount > 0)
            {
                CliUi.WriteSuccess(
                    $"Scanned {result.FilesScanned} file(s) across {result.ProjectsScanned} project(s) — "
                    + $"{dismissedSkippedCount} finding(s) skipped (previously dismissed).");
                return;
            }

            CliUi.WriteSuccess(
                $"Scanned {result.FilesScanned} file(s) across {result.ProjectsScanned} project(s) — no LINQ performance warnings.");
            return;
        }

        WriteSummaryPanel(
            ":scan lite",
            result,
            savedPath,
            "static heuristics only",
            "Results saved — review one finding at a time below",
            dismissedSkippedCount);
    }

    internal static void WriteDeepSummary(
        LinqLiteScanResult result,
        string displayRootDirectory,
        string savedPath,
        LinqDeepScanStats? deepStats,
        int dismissedSkippedCount = 0)
    {
        if (result.Findings.Count == 0 && deepStats is { QuerySitesVisited: 0 })
        {
            if (dismissedSkippedCount > 0)
            {
                CliUi.WriteSuccess(
                    $"Scanned {result.FilesScanned} file(s) across {result.ProjectsScanned} project(s) — "
                    + $"{dismissedSkippedCount} finding(s) skipped (previously dismissed).");
                return;
            }

            CliUi.WriteSuccess(
                $"Scanned {result.FilesScanned} file(s) across {result.ProjectsScanned} project(s) — no query sites found.");
            return;
        }

        if (result.Findings.Count == 0 && dismissedSkippedCount > 0)
        {
            CliUi.WriteSuccess(
                $"Scan complete — {dismissedSkippedCount} finding(s) skipped (previously dismissed).");
            return;
        }

        var sqlLine = deepStats is null
            ? "SQL translation stats unavailable"
            : $"SQL translated [cyan]{deepStats.SqlTranslatedCount}[/]/[white]{deepStats.QuerySitesVisited}[/] site(s)"
              + (deepStats.SqlFailedCount > 0
                  ? $" · [yellow]{deepStats.SqlFailedCount}[/] could not translate"
                  : string.Empty)
              + (deepStats.QueryPlanCount > 0
                  ? $" · [green]{deepStats.QueryPlanCount}[/] EXPLAIN plan(s)"
                  : string.Empty)
              + (deepStats.QueryPlanFailedCount > 0
                  ? $" · [yellow]{deepStats.QueryPlanFailedCount}[/] plan(s) failed"
                  : string.Empty);

        WriteSummaryPanel(
            ":scan deep",
            result,
            savedPath,
            sqlLine,
            "Heuristics + ToQueryString + EXPLAIN per call site — review queue below",
            dismissedSkippedCount);
    }

    internal static void WriteUsage()
    {
        CliUi.WriteWarning("Usage: :scan lite | :scan deep");
        AnsiConsole.MarkupLine("[grey]:scan lite[/] — static Roslyn heuristics");
        AnsiConsole.MarkupLine(
            "[grey]:scan deep[/] — lite scan + translated SQL + EXPLAIN via live [cyan]db[/] (requires connection)");
    }

    private static void WriteSummaryPanel(
        string title,
        LinqLiteScanResult result,
        string savedPath,
        string detailLine,
        string footerLine,
        int dismissedSkippedCount)
    {
        var fileCount = result.Findings
            .Select(static finding => finding.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var dismissedLine = dismissedSkippedCount > 0
            ? $"\n[grey]{dismissedSkippedCount} previously dismissed finding(s) excluded[/]"
            : string.Empty;

        var criticalCount = result.Findings.Count(static f => f.Severity == LinqScanSeverity.Critical);
        var errorCount = result.Findings.Count(static f => f.Severity == LinqScanSeverity.Error);
        var warningCount = result.Findings.Count(static f => f.Severity == LinqScanSeverity.Warning);
        var infoCount = result.Findings.Count(static f => f.Severity == LinqScanSeverity.Info);

        var severityLine = result.Findings.Count == 0
            ? string.Empty
            : $"\n[grey]Severity:[/] [red]{criticalCount} critical[/] · [red]{errorCount} error[/] · [yellow]{warningCount} warning[/] · [cyan]{infoCount} info[/]";

        AnsiConsole.WriteLine();

        AnsiConsole.Write(
            new Panel(
                new Markup(
                    $"[bold]{title}[/] — [yellow]{result.Findings.Count}[/] finding(s) in "
                    + $"[cyan]{fileCount}[/] file(s)\n"
                    + $"[grey]{result.FilesScanned} source file(s) · {result.ProjectsScanned} project(s) · {detailLine}[/]"
                    + severityLine
                    + dismissedLine
                    + $"\n[grey]{footerLine}[/]"))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0)
            });

        AnsiConsole.WriteLine();
    }
}