using Spectre.Console;

namespace MyEfVibe;

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
        body.AddRow("[grey]Message[/]", Markup.Escape(finding.Message));
        body.AddRow("[grey]Fix[/]", Markup.Escape(finding.ResolvedRecommendation));
        body.AddRow("[grey]Code[/]", $"[dim]{Markup.Escape(finding.Code)}[/]");

        AnsiConsole.Write(
            new Panel(body)
            {
                Header = new PanelHeader($"[bold]Finding {index + 1} of {total}[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0),
            });

        AnsiConsole.WriteLine();
    }

    internal static void WriteNavigationHint(string savedPath)
    {
        AnsiConsole.MarkupLine(
            $"[grey]Saved to[/] [cyan]{Markup.Escape(savedPath)}[/]");
        AnsiConsole.MarkupLine(
            "[grey]:next[/] or [grey]→[/] next · [grey]:prev[/] or [grey]←[/] previous · "
            + "[grey]:repeat[/] restart · [grey]:end[/] exit review");
        AnsiConsole.WriteLine();
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
