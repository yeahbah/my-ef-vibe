using Spectre.Console;

namespace MyEfVibe.Linq;

internal static class LinqScanReviewPresenter
{
    internal static void Show(LinqScanFinding finding, int index, int total, string displayRootDirectory)
    {
        var normalizedRoot = Path.GetFullPath(displayRootDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var relativePath = TryToRelativePath(normalizedRoot, finding.FilePath);

        AnsiConsole.WriteLine();

        var body = new Grid();
        body.AddColumn();
        body.AddColumn();
        body.AddRow("[grey]File[/]", $"[cyan]{Markup.Escape(relativePath)}[/]");
        body.AddRow("[grey]Line[/]", $"[white]{finding.Line}[/]");
        body.AddRow("[grey]Rule[/]", $"[dim]{Markup.Escape(finding.RuleId)}[/]");
        body.AddRow("[grey]Severity[/]", FormatSeverityMarkup(finding.Severity));
        body.AddRow("[grey]Message[/]", Markup.Escape(finding.Message));

        if (!string.IsNullOrWhiteSpace(finding.SavedNote))
        {
            body.AddRow("[grey]Note[/]", $"[bold yellow]{Markup.Escape(finding.SavedNote)}[/]");
        }

        body.AddRow("[grey]Fix[/]", Markup.Escape(finding.ResolvedRecommendation));
        body.AddRow("[grey]Code[/]", $"[dim]{Markup.Escape(finding.Code)}[/]");

        if (!string.IsNullOrWhiteSpace(finding.SqlTranslationNote) && string.IsNullOrWhiteSpace(finding.TranslatedSql))
        {
            body.AddRow("[grey]SQL[/]", $"[yellow]{Markup.Escape(finding.SqlTranslationNote)}[/]");
        }

        AnsiConsole.Write(
            new Panel(body)
            {
                Header = new PanelHeader($"[bold]Finding {index + 1} of {total}[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0)
            });

        if (!string.IsNullOrWhiteSpace(finding.TranslatedSql))
        {
            CliUi.WriteSqlBlock("Translated SQL (ToQueryString)", finding.TranslatedSql);
        }

        if (!string.IsNullOrWhiteSpace(finding.QueryPlan))
        {
            CliUi.WriteSqlBlock("Query plan (EXPLAIN)", finding.QueryPlan);
        }
        else if (!string.IsNullOrWhiteSpace(finding.QueryPlanNote)
                 && !string.IsNullOrWhiteSpace(finding.TranslatedSql))
        {
            CliUi.WriteSqlBlock("Query plan", finding.QueryPlanNote);
        }

        if (string.IsNullOrWhiteSpace(finding.TranslatedSql)
            && string.IsNullOrWhiteSpace(finding.QueryPlan))
        {
            AnsiConsole.WriteLine();
        }
    }

    internal static void WriteNavigationHint(string savedPath)
    {
        AnsiConsole.MarkupLine(
            $"[grey]Saved to[/] [cyan]{Markup.Escape(savedPath)}[/]");
        AnsiConsole.MarkupLine(
            "[grey]:next[/] or [grey]→[/] next · [grey]:prev[/] or [grey]←[/] previous · "
            + "[grey]:dismiss[/] or [grey]Del[/] skip in future scans · "
            + "[grey]:note[/] save note · "
            + "[grey]:repeat[/] restart · [grey]:end[/] exit review");
        AnsiConsole.WriteLine();
    }

    private static string FormatSeverityMarkup(LinqScanSeverity severity)
    {
        return severity switch
        {
            LinqScanSeverity.Critical => "[bold red]critical[/]",
            LinqScanSeverity.Error => "[bold red]error[/]",
            LinqScanSeverity.Warning => "[bold yellow]warning[/]",
            _ => "[dim cyan]info[/]"
        };
    }

    private static string TryToRelativePath(string rootDirectory, string absolutePath)
    {
        try
        {
            return Path.GetRelativePath(rootDirectory, absolutePath);
        }
        catch
        {
            return absolutePath;
        }
    }
}