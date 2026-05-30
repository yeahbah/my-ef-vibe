using Spectre.Console;

namespace MyEfVibe;

internal static class CliUi
{
    internal const string PrimaryPrompt = "[bold cyan]❯[/] ";
    internal const string ContinuationPrompt = "[grey]…[/] ";

    internal static string ScanReviewPrompt(int oneBasedIndex, int total)
    {
        return $"[bold yellow][[scan {oneBasedIndex}/{total}]][/] [bold cyan]❯[/] ";
    }

    internal static int GetVisiblePromptWidth(string markupPrompt)
    {
        return markupPrompt switch
        {
            PrimaryPrompt => 2,
            ContinuationPrompt => 2,
            _ => StripMarkup(markupPrompt).Length
        };
    }

    internal static void Configure()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        AnsiConsole.Profile.Width = Math.Max(AnsiConsole.Profile.Width, 100);
        AnsiConsole.Profile.Height = Math.Max(AnsiConsole.Profile.Height, 24);
    }

    internal static void WriteBanner()
    {
        StartupBanner.Write();
    }

    internal static void WriteRule(string? title = null)
    {
        var rule = new Rule(string.IsNullOrWhiteSpace(title) ? " " : $"[grey]{title}[/]")
        {
            Style = new Style(Color.Grey)
        };

        AnsiConsole.Write(rule);
    }

    internal static void WriteSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(message)}");
    }

    internal static void ClearScreen()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        AnsiConsole.Clear();
    }

    internal static void WriteWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]![/] {Markup.Escape(message)}");
    }

    internal static void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(message)}");
    }

    internal static void WriteErrorPanel(string title, string content)
    {
        AnsiConsole.Write(
            new Panel(Markup.Escape(content))
            {
                Header = new PanelHeader($"[bold red]{Markup.Escape(title)}[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Red),
                Padding = new Padding(1, 0, 1, 0)
            });

        AnsiConsole.WriteLine();
    }

    internal static void WriteSessionPanel(string contextTypeName, string projectLabel, DbLogSettings dbLogSettings)
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow("[grey]DbContext[/]", $"[cyan]{Markup.Escape(contextTypeName)}[/]");
        grid.AddRow("[grey]Project[/]", $"[white]{Markup.Escape(projectLabel)}[/]");
        grid.AddRow(
            "[grey]Db log[/]",
            dbLogSettings.Enabled
                ? $"[green]on[/] [grey]({DbLogLevelParser.Format(dbLogSettings.Level)} · {DbLogCommandParser.FormatMode(dbLogSettings)} · :dblog)[/]"
                : "[grey]off[/] [grey](:dblog on|off [[level]] [[verbose]])[/]");
        grid.AddRow(
            "[grey]Input[/]",
            "[grey]Enter next line · ; run · Shift+Enter newline · Tab complete[/]");

        AnsiConsole.Write(
            new Panel(grid)
            {
                Header = new PanelHeader("[bold]Session[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0)
            });

        AnsiConsole.WriteLine();
    }

    internal static void WriteHelpTable()
    {
        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Command[/]").NoWrap())
            .AddColumn("[grey]Description[/]");

        table.AddRow("[cyan]:help[/]", "Show this help");
        table.AddRow("[cyan]:about[/]", "Tool version, license, and current session info");
        table.AddRow("[cyan]:clear[/]", "Clear the terminal screen");
        table.AddRow("[cyan]:reset[/]", "Clear script variables ([grey]db[/] stays)");
        table.AddRow(
            "[cyan]:dblog[/]",
            "Database logging (sql-only by default) — :dblog on|off [[level]] [[verbose]]");
        table.AddRow("[cyan]:stats[/]", "Session evaluation statistics");
        table.AddRow("[cyan]:tracked[/]", "Change tracker summary");
        table.AddRow("[cyan]:tables[/]", "List DbSets and entity types");
        table.AddRow("[cyan]:dbinfo[/]", "Database, provider, and connection details");
        table.AddRow("[cyan]:describe <entity>[/]", "Entity properties (DbSet name or type)");
        table.AddRow("[cyan]:scan lite[/]", "Static LINQ performance scan of EF project sources");
        table.AddRow("[cyan]:scan deep[/]", "Lite scan + ToQueryString + EXPLAIN per call site (live db)");
        table.AddRow("[cyan]:next[/] · [cyan]:prev[/]", "Step through scan findings (also → / ← on empty prompt)");
        table.AddRow("[cyan]:dismiss[/] [[note]]",
            "Skip finding in future scans; Del on empty prompt during scan review");
        table.AddRow("[cyan]:note[/] text…", "Save a required note on the current finding (shown on next scan)");
        table.AddRow("[cyan]:repeat[/] · [cyan]:end[/]", "Restart scan review queue · exit scan review");
        table.AddRow("[cyan]:plan[/]", "EXPLAIN last database log SQL");
        table.AddRow("[cyan]:compare set[/]", "Save baseline · [cyan]:compare[/] diff last run");
        table.AddRow("[cyan]:history stats[/]", "History with timings");
        table.AddRow("[cyan]:benchmark N[/]", "Repeat last query N times");
        table.AddRow("[cyan]:chart[/]", "Charts: stats · timing · compare · tables · result");
        table.AddRow("[cyan]:export csv|json[/]", "Export last result");
        table.AddRow("[cyan]:warnings[/]", "Show warnings for last snippet");
        table.AddRow("[cyan]:quit[/]", "Exit the session");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Examples[/]");

        var examples = new List<string>
        {
            "db.JsonBlobDocuments.Count()",
            "db.JsonBlobDocuments.AsNoTracking().Take(5).ToList()",
            "var q = db.Orders.Where(o => o.Total > 100)",
            "q.Count()"
        };

        foreach (var example in examples)
        {
            AnsiConsole.MarkupLine($"  [grey]›[/] [cyan]{Markup.Escape(example)}[/]");
        }

        AnsiConsole.WriteLine();
    }

    internal static void WriteSqlBlock(string heading, string sql)
    {
        AnsiConsole.Write(
            new Panel(new Text(sql))
            {
                Header = new PanelHeader($"[yellow]{Markup.Escape(heading)}[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Yellow),
                Padding = new Padding(1, 0, 1, 0)
            });

        AnsiConsole.WriteLine();
    }

    internal static void WriteTiming(long milliseconds)
    {
        AnsiConsole.MarkupLine($"[grey]{milliseconds} ms[/]");
    }

    internal static void WriteResult(object? value)
    {
        ResultPresenter.Present(value);
    }

    internal static void WritePrompt(string markupPrompt)
    {
        if (Console.IsOutputRedirected)
        {
            Console.Write(StripMarkup(markupPrompt));
        }
        else
        {
            AnsiConsole.Markup(markupPrompt);
        }
    }

    internal static void WriteGoodbye()
    {
        AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
    }

    internal static T RunWithStatus<T>(string message, Func<T> action)
    {
        T? result = default;

        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots2)
            .SpinnerStyle(Style.Parse("cyan"))
            .Start(
                Markup.Escape(message),
                _ => { result = action(); });

        return result!;
    }

    private static string StripMarkup(string markup)
    {
        return markup.Replace("[bold cyan]❯[/] ", "> ", StringComparison.Ordinal)
            .Replace("[grey]…[/] ", ".. ", StringComparison.Ordinal);
    }
}