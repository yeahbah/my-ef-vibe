using Spectre.Console;

namespace MyEfVibe;

internal static class AnalyticsPresenter
{
    internal static void WriteEvaluation(
        object? result,
        EvaluationMetrics metrics,
        DbLogSettings dbLogSettings,
        bool useSpectre)
    {
        if (dbLogSettings.Enabled)
            WriteSql(metrics, dbLogSettings, useSpectre);

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

    internal static void WriteSql(EvaluationMetrics metrics, DbLogSettings dbLogSettings, bool useSpectre)
    {
        if (metrics.ExecutedSql.Count == 0 && string.IsNullOrWhiteSpace(metrics.TranslatedSql))
            return;

        if (useSpectre && !Console.IsOutputRedirected)
        {
            WriteSqlBlocks(metrics, dbLogSettings, CliUi.WriteSqlBlock);
            AnsiConsole.WriteLine();
            return;
        }

        WriteSqlBlocks(
            metrics,
            dbLogSettings,
            static (title, sql) =>
            {
                Console.WriteLine($"{title}:");
                Console.WriteLine(sql);
            });
    }

    private static void WriteSqlBlocks(
        EvaluationMetrics metrics,
        DbLogSettings dbLogSettings,
        Action<string, string> writeBlock)
    {
        if (metrics.ExecutedSql.Count > 0)
        {
            var blockTitle = dbLogSettings.Verbose ? "Database log (verbose)" : "Database log";

            foreach (var sql in metrics.ExecutedSql)
                writeBlock(blockTitle, sql);

            return;
        }

        if (!string.IsNullOrWhiteSpace(metrics.TranslatedSql))
            writeBlock("Translated SQL", metrics.TranslatedSql);
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
            parts.Add($"{metrics.SqlCommandCount} command(s)");

        if (metrics.RowCount is not null)
            parts.Add($"{metrics.RowCount} row(s)");

        if (metrics.EstimatedBytes is not null)
            parts.Add(FormatBytes(metrics.EstimatedBytes.Value));

        return string.Join(" · ", parts);
    }

    internal static void WriteWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
            return;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Warnings[/]");

        foreach (var warning in warnings)
            AnsiConsole.MarkupLine($"  [yellow]![/] {Markup.Escape(warning)}");
    }

    internal static void WriteSessionStats(IReadOnlyList<EvaluationMetrics> evaluations)
    {
        if (evaluations.Count == 0)
        {
            CliUi.WriteWarning("No evaluations yet.");
            return;
        }

        var succeeded = evaluations.Count(static metrics => metrics.Succeeded);
        var totalMs = evaluations.Sum(static metrics => metrics.TotalMilliseconds);
        var totalDbMs = evaluations.Sum(static metrics => metrics.DatabaseMilliseconds ?? 0);
        var totalCommands = evaluations.Sum(static metrics => metrics.SqlCommandCount);

        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("[grey]Metric[/]")
            .AddColumn("[grey]Value[/]");

        table.AddRow("Evaluations", evaluations.Count.ToString());
        table.AddRow("Succeeded", succeeded.ToString());
        table.AddRow("Total ms", totalMs.ToString());
        table.AddRow("Database ms", totalDbMs.ToString());
        table.AddRow("SQL commands", totalCommands.ToString());

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    internal static void WriteCompare(EvaluationMetrics baseline, EvaluationMetrics current)
    {
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

        var baselineSql = FormatExecutedSql(baseline);
        var currentSql = FormatExecutedSql(current);

        if (!string.IsNullOrWhiteSpace(baselineSql) || !string.IsNullOrWhiteSpace(currentSql))
        {
            AnsiConsole.MarkupLine("[bold]SQL diff[/]");

            if (string.Equals(baselineSql, currentSql, StringComparison.Ordinal))
                AnsiConsole.MarkupLine("[green]Database log SQL unchanged.[/]");
            else
            {
                CliUi.WriteSqlBlock("baseline", baselineSql ?? "(none)");
                CliUi.WriteSqlBlock("current", currentSql ?? "(none)");
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

    internal static string? GetPlanSql(EvaluationMetrics? metrics)
    {
        if (metrics is null)
            return null;

        return DbLogSqlExtractor.SelectPlanSql(metrics.ExecutedSql, metrics.TranslatedSql);
    }

    private static string? FormatExecutedSql(EvaluationMetrics metrics) =>
        metrics.ExecutedSql.Count == 0
            ? metrics.TranslatedSql
            : string.Join(Environment.NewLine + Environment.NewLine, metrics.ExecutedSql);

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
