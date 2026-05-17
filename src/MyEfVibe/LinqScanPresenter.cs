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

        var fileCount = result.Findings
            .Select(static finding => finding.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        AnsiConsole.WriteLine();

        AnsiConsole.Write(
            new Panel(
                new Markup(
                    $"[bold]:scan lite[/] — [yellow]{result.Findings.Count}[/] finding(s) in "
                    + $"[cyan]{fileCount}[/] file(s)\n"
                    + $"[grey]{result.FilesScanned} source file(s) · {result.ProjectsScanned} project(s) · static heuristics only[/]\n"
                    + $"[grey]Results saved — review one finding at a time below[/]"))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0),
            });

        AnsiConsole.WriteLine();
    }

    internal static void WriteUsage()
    {
        CliUi.WriteWarning("Usage: :scan lite");
        AnsiConsole.MarkupLine("[grey]Future:[/] :scan deep (SQL / ToQueryString per call site)");
    }
}
