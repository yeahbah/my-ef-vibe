using Spectre.Console;

namespace MyEfVibe;

internal static class LinqScanPresenter
{
    internal static void WriteLiteSummary(LinqLiteScanResult result, string displayRootDirectory, string savedPath)
    {
        if (result.Findings.Count == 0)
        {
            CliUi.WriteSuccess(
                $"Scanned {result.FilesScanned} file(s) across {result.ProjectsScanned} project(s) — no LINQ performance warnings.");
            return;
        }

        WriteSummaryPanel(
            ":scan lite",
            result,
            savedPath,
            "static heuristics only",
            "Results saved — review one finding at a time below");
    }

    internal static void WriteDeepSummary(
        LinqLiteScanResult result,
        string displayRootDirectory,
        string savedPath,
        LinqDeepScanStats? deepStats)
    {
        if (result.Findings.Count == 0 && deepStats is { QuerySitesVisited: 0 })
        {
            CliUi.WriteSuccess(
                $"Scanned {result.FilesScanned} file(s) across {result.ProjectsScanned} project(s) — no query sites found.");
            return;
        }

        var sqlLine = deepStats is null
            ? "SQL translation stats unavailable"
            : $"SQL translated [cyan]{deepStats.SqlTranslatedCount}[/]/[white]{deepStats.QuerySitesVisited}[/] site(s)"
              + (deepStats.SqlFailedCount > 0
                  ? $" · [yellow]{deepStats.SqlFailedCount}[/] could not translate"
                  : string.Empty);

        WriteSummaryPanel(
            ":scan deep",
            result,
            savedPath,
            sqlLine,
            "Heuristics + ToQueryString per call site — review queue below");
    }

    internal static void WriteUsage()
    {
        CliUi.WriteWarning("Usage: :scan lite | :scan deep");
        AnsiConsole.MarkupLine("[grey]:scan lite[/] — static Roslyn heuristics");
        AnsiConsole.MarkupLine("[grey]:scan deep[/] — lite scan plus translated SQL via live [cyan]db[/] (requires connection)");
    }

    private static void WriteSummaryPanel(
        string title,
        LinqLiteScanResult result,
        string savedPath,
        string detailLine,
        string footerLine)
    {
        var fileCount = result.Findings
            .Select(static finding => finding.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        AnsiConsole.WriteLine();

        AnsiConsole.Write(
            new Panel(
                new Markup(
                    $"[bold]{title}[/] — [yellow]{result.Findings.Count}[/] finding(s) in "
                    + $"[cyan]{fileCount}[/] file(s)\n"
                    + $"[grey]{result.FilesScanned} source file(s) · {result.ProjectsScanned} project(s) · {detailLine}[/]\n"
                    + $"[grey]{footerLine}[/]"))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0),
            });

        AnsiConsole.WriteLine();
    }
}
