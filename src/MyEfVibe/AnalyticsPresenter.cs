using Spectre.Console;

namespace MyEfVibe;

internal static class AnalyticsPresenter
{
    internal static void WriteEvaluation(
        object? result,
        EvaluationMetrics metrics,
        SqlDisplaySettings sqlSettings,
        bool useSpectre)
    {
        if (sqlSettings.ShowSql)
            WriteSql(metrics, useSpectre);

        if (useSpectre && !Console.IsOutputRedirected)
        {
            CliUi.WriteResult(result);
            WriteFooter(metrics);
            WriteWarnings(metrics.Warnings);
        }
        else
        {
            var writer = Console.Out;
            writer.WriteLine(FormatFooter(metrics));

            if (metrics.Warnings.Count > 0)
            {
                writer.WriteLine("Warnings:");

                foreach (var warning in metrics.Warnings)
                    writer.WriteLine($"  - {warning}");
            }

            ResultPresenter.Present(result, writer);
        }
    }

    internal static void WriteSql(EvaluationMetrics metrics, bool useSpectre)
    {
        if (useSpectre && !Console.IsOutputRedirected)
        {
            if (!string.IsNullOrWhiteSpace(metrics.TranslatedSql))
                CliUi.WriteSqlBlock("Translated SQL", metrics.TranslatedSql);

            foreach (var sql in metrics.ExecutedSql)
                CliUi.WriteSqlBlock("Executed SQL", sql);

            if (metrics.TranslatedSql is not null || metrics.ExecutedSql.Count > 0)
                AnsiConsole.WriteLine();

            return;
        }

        if (!string.IsNullOrWhiteSpace(metrics.TranslatedSql))
        {
            Console.WriteLine("Translated SQL:");
            Console.WriteLine(metrics.TranslatedSql);
        }

        foreach (var sql in metrics.ExecutedSql)
        {
            Console.WriteLine("Executed SQL:");
            Console.WriteLine(sql);
        }
    }

    internal static void WriteFooter(EvaluationMetrics metrics)
    {
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(FormatFooter(metrics))}[/]");
    }

    internal static string FormatFooter(EvaluationMetrics metrics)
    {
        var parts = new List<string> { $"{metrics.TotalMilliseconds} ms" };

        if (metrics.DatabaseMilliseconds is not null)
            parts.Add($"db {metrics.DatabaseMilliseconds} ms");

        if (metrics.SqlCommandCount > 0)
            parts.Add($"{metrics.SqlCommandCount} cmd");

        parts.Add(DescribeRows(metrics));
        parts.Add(metrics.IsMaterialized ? "materialized" : "deferred");

        if (metrics.EstimatedBytes is not null)
            parts.Add($"~{FormatBytes(metrics.EstimatedBytes.Value)}");

        return string.Join(" · ", parts);
    }

    internal static void WriteWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
            return;

        foreach (var warning in warnings)
            AnsiConsole.MarkupLine($"[yellow]![/] {Markup.Escape(warning)}");
    }

    internal static void WriteSessionStats(IReadOnlyList<EvaluationMetrics> evaluations)
    {
        if (evaluations.Count == 0)
        {
            CliUi.WriteWarning("No evaluations yet.");
            return;
        }

        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("#")
            .AddColumn("ms")
            .AddColumn("db")
            .AddColumn("cmd")
            .AddColumn("rows")
            .AddColumn("kind")
            .AddColumn("snippet");

        var index = 1;

        foreach (var metrics in evaluations.TakeLast(20))
        {
            table.AddRow(
                index.ToString(),
                metrics.TotalMilliseconds.ToString(),
                metrics.DatabaseMilliseconds?.ToString() ?? "-",
                metrics.SqlCommandCount.ToString(),
                metrics.RowCount?.ToString() ?? "-",
                metrics.ResultKind.ToString(),
                Truncate(metrics.Snippet.ReplaceLineEndings(" "), 40));

            index++;
        }

        AnsiConsole.Write(table);

        var succeeded = evaluations.Where(static metrics => metrics.Succeeded).ToArray();

        if (succeeded.Length > 0)
        {
            AnsiConsole.MarkupLine(
                $"[grey]Session: {evaluations.Count} runs · " +
                $"avg {succeeded.Average(static metrics => metrics.TotalMilliseconds):F0} ms · " +
                $"max {succeeded.Max(static metrics => metrics.TotalMilliseconds)} ms · " +
                $"{succeeded.Sum(static metrics => metrics.SqlCommandCount)} SQL commands[/]");
        }

        AnsiConsole.WriteLine();
    }

    internal static void WriteCompare(EvaluationMetrics? baseline, EvaluationMetrics? current)
    {
        if (baseline is null || current is null)
        {
            CliUi.WriteWarning("Need two evaluations. Run a query, then `:compare set`, change and run again, then `:compare`.");
            return;
        }

        var table = new Table().RoundedBorder().BorderColor(Color.Grey);
        table.AddColumn("");
        table.AddColumn("baseline");
        table.AddColumn("current");

        table.AddRow("ms", baseline.TotalMilliseconds.ToString(), current.TotalMilliseconds.ToString());
        table.AddRow("db ms", baseline.DatabaseMilliseconds?.ToString() ?? "-", current.DatabaseMilliseconds?.ToString() ?? "-");
        table.AddRow("commands", baseline.SqlCommandCount.ToString(), current.SqlCommandCount.ToString());
        table.AddRow("rows", baseline.RowCount?.ToString() ?? "-", current.RowCount?.ToString() ?? "-");
        table.AddRow("kind", baseline.ResultKind.ToString(), current.ResultKind.ToString());

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (!string.IsNullOrWhiteSpace(baseline.TranslatedSql) || !string.IsNullOrWhiteSpace(current.TranslatedSql))
        {
            AnsiConsole.MarkupLine("[bold]SQL diff[/]");

            if (string.Equals(baseline.TranslatedSql, current.TranslatedSql, StringComparison.Ordinal))
                AnsiConsole.MarkupLine("[green]Translated SQL unchanged.[/]");
            else
            {
                CliUi.WriteSqlBlock("baseline", baseline.TranslatedSql ?? "(none)");
                CliUi.WriteSqlBlock("current", current.TranslatedSql ?? "(none)");
            }
        }

        AnsiConsole.WriteLine();
    }

    internal static void WriteHistoryStats(IReadOnlyList<string> history, IReadOnlyList<EvaluationMetrics> evaluations)
    {
        if (history.Count == 0)
        {
            CliUi.WriteWarning("No history yet.");
            return;
        }

        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("#")
            .AddColumn("snippet")
            .AddColumn("ms");

        for (var index = 0; index < history.Count; index++)
        {
            var metrics = index < evaluations.Count ? evaluations[index] : null;

            table.AddRow(
                (index + 1).ToString(),
                Truncate(history[index].ReplaceLineEndings(" "), 50),
                metrics?.TotalMilliseconds.ToString() ?? "-");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static string DescribeRows(EvaluationMetrics metrics) =>
        metrics.ResultKind switch
        {
            ResultKind.Null => "null",
            ResultKind.Queryable => "IQueryable",
            ResultKind.Enumerable => metrics.RowCount is null ? "sequence" : $"{metrics.RowCount} rows",
            _ => metrics.RowCount is null ? metrics.ResultKind.ToString() : $"{metrics.RowCount} row(s)",
        };

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
        };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}
