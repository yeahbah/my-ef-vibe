using System.Globalization;
using System.Reflection;
using Spectre.Console;

namespace MyEfVibe;

internal static class VisualizationPresenter
{
    internal static void WriteHelp()
    {
        var table = new Table().RoundedBorder().BorderColor(Color.Grey);
        table.AddColumn("subcommand");
        table.AddColumn("chart");

        table.AddRow("stats", "Bar chart of recent evaluation times (ms)");
        table.AddRow("timing", "Breakdown of last run: DB vs app time");
        table.AddRow("compare", "Bar chart: baseline vs current");
        table.AddRow("tables", "Bar chart of DbSet row counts");
        table.AddRow("result", "Bar chart of numeric values from last result");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    internal static void WriteSessionTimings(IReadOnlyList<EvaluationMetrics> evaluations)
    {
        if (!EnsureInteractive())
            return;

        var recent = evaluations
            .Where(static metrics => metrics.Succeeded)
            .TakeLast(12)
            .ToArray();

        if (recent.Length == 0)
        {
            CliUi.WriteWarning("No successful evaluations to chart.");
            return;
        }

        var chart = new BarChart()
            .Width(Console.WindowWidth > 0 ? Math.Min(Console.WindowWidth - 4, 72) : 60)
            .Label("[bold]Session timings[/] [grey](ms)[/]");

        var index = evaluations.Count - recent.Length + 1;

        foreach (var metrics in recent)
        {
            var label = $"#{index}";
            chart.AddItem(label, metrics.TotalMilliseconds, Color.Cyan1);
            index++;
        }

        AnsiConsole.Write(chart);
        AnsiConsole.WriteLine();
    }

    internal static void WriteLastTimingBreakdown(EvaluationMetrics? metrics)
    {
        if (!EnsureInteractive())
            return;

        if (metrics is null || !metrics.Succeeded)
        {
            CliUi.WriteWarning("No successful evaluation to chart.");
            return;
        }

        var dbMs = Math.Max(0, metrics.DatabaseMilliseconds ?? 0);
        var appMs = Math.Max(0, metrics.TotalMilliseconds - dbMs);

        if (dbMs == 0 && appMs == 0)
        {
            CliUi.WriteWarning("No timing data for the last evaluation.");
            return;
        }

        var chart = new BreakdownChart()
            .Width(Console.WindowWidth > 0 ? Math.Min(Console.WindowWidth - 4, 60) : 50)
            .ShowTags();

        if (dbMs > 0)
            chart.AddItem("database", dbMs, Color.Yellow);

        if (appMs > 0)
            chart.AddItem("app / roslyn", appMs, Color.Cyan1);

        AnsiConsole.MarkupLine("[bold]Last evaluation time[/]");
        AnsiConsole.Write(chart);
        AnsiConsole.MarkupLine(
            $"[grey]{metrics.TotalMilliseconds} ms total · {metrics.SqlCommandCount} SQL command(s)[/]");
        AnsiConsole.WriteLine();
    }

    internal static void WriteCompare(EvaluationMetrics? baseline, EvaluationMetrics? current)
    {
        if (!EnsureInteractive())
            return;

        if (baseline is null || current is null)
        {
            CliUi.WriteWarning("Need baseline and current. Use `:compare set` after first query, then run a second.");
            return;
        }

        var chart = new BarChart()
            .Width(Console.WindowWidth > 0 ? Math.Min(Console.WindowWidth - 4, 50) : 40)
            .Label("[bold]Total time (ms)[/]");

        chart.AddItem("baseline", baseline.TotalMilliseconds, Color.Grey);
        chart.AddItem("current", current.TotalMilliseconds, Color.Cyan1);

        AnsiConsole.Write(chart);

        if (baseline.DatabaseMilliseconds is not null || current.DatabaseMilliseconds is not null)
        {
            var dbChart = new BarChart()
                .Width(Console.WindowWidth > 0 ? Math.Min(Console.WindowWidth - 4, 50) : 40)
                .Label("[bold]Database time (ms)[/]");

            dbChart.AddItem("baseline", baseline.DatabaseMilliseconds ?? 0, Color.Grey);
            dbChart.AddItem("current", current.DatabaseMilliseconds ?? 0, Color.Yellow);

            AnsiConsole.Write(dbChart);
        }

        AnsiConsole.WriteLine();
    }

    internal static void WriteBenchmarkTimings(IReadOnlyList<long> timingsMs)
    {
        if (!EnsureInteractive() || timingsMs.Count == 0)
            return;

        var chart = new BarChart()
            .Width(Console.WindowWidth > 0 ? Math.Min(Console.WindowWidth - 4, 60) : 50)
            .Label("[bold]Benchmark iterations[/] [grey](ms)[/]");

        for (var index = 0; index < timingsMs.Count; index++)
            chart.AddItem($"#{index + 1}", timingsMs[index], Color.Cyan1);

        AnsiConsole.Write(chart);
        AnsiConsole.WriteLine();
    }

    internal static void WriteTableRowCounts(IReadOnlyList<(string DbSet, string EntityType, int? Count)> sets)
    {
        if (!EnsureInteractive())
            return;

        var countable = sets
            .Where(static set => set.Count is not null)
            .ToArray();

        if (countable.Length == 0)
        {
            CliUi.WriteWarning("No row counts available to chart.");
            return;
        }

        var chart = new BarChart()
            .Width(Console.WindowWidth > 0 ? Math.Min(Console.WindowWidth - 4, 72) : 60)
            .Label("[bold]DbSet row counts[/]");

        foreach (var set in countable)
            chart.AddItem(set.DbSet, set.Count!.Value, Color.Green);

        AnsiConsole.Write(chart);
        AnsiConsole.WriteLine();
    }

    internal static void WriteResultNumeric(IReadOnlyList<object?> rows)
    {
        if (!EnsureInteractive())
            return;

        if (rows.Count == 0)
        {
            CliUi.WriteWarning("No rows in the last result.");
            return;
        }

        if (rows.Count > 25)
        {
            CliUi.WriteWarning("Result has more than 25 rows — chart supports at most 25.");
            return;
        }

        if (!TryPickNumericColumn(rows, out var column, out var values))
        {
            CliUi.WriteWarning("No numeric column found in the last result to chart.");
            return;
        }

        var chart = new BarChart()
            .Width(Console.WindowWidth > 0 ? Math.Min(Console.WindowWidth - 4, 72) : 60)
            .Label($"[bold]{Markup.Escape(column)}[/] [grey](last result)[/]");

        for (var index = 0; index < values.Count; index++)
        {
            var label = $"#{index + 1}";
            chart.AddItem(label, values[index], Color.Cyan1);
        }

        AnsiConsole.Write(chart);
        AnsiConsole.MarkupLine($"[grey]Column: {Markup.Escape(column)} · {values.Count} value(s)[/]");
        AnsiConsole.WriteLine();
    }

    private static bool TryPickNumericColumn(
        IReadOnlyList<object?> rows,
        out string column,
        out IReadOnlyList<double> values)
    {
        column = string.Empty;
        values = Array.Empty<double>();

        var first = rows.FirstOrDefault(static row => row is not null);

        if (first is null)
            return false;

        foreach (var property in GetReadableProperties(first))
        {
            var numbers = new List<double>();

            foreach (var row in rows)
            {
                if (row is null)
                    continue;

                var raw = ReadProperty(row, property);

                if (!TryToDouble(raw, out var number))
                {
                    numbers.Clear();
                    break;
                }

                numbers.Add(number);
            }

            if (numbers.Count == rows.Count(r => r is not null) && numbers.Count > 0)
            {
                column = property.Name;
                values = numbers;
                return true;
            }
        }

        if (rows.All(static row => row is null || IsNumericScalar(row)))
        {
            column = "value";
            values = rows
                .Where(static row => row is not null)
                .Select(static row => TryToDouble(row, out var number) ? number : 0)
                .ToArray();

            return values.Count > 0;
        }

        return false;
    }

    private static IEnumerable<PropertyInfo> GetReadableProperties(object row) =>
        row.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
            .OrderBy(static property => property.Name, StringComparer.Ordinal);

    private static object? ReadProperty(object row, PropertyInfo property)
    {
        try
        {
            return property.GetValue(row);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsNumericScalar(object value)
    {
        var type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();

        return type.IsPrimitive && type != typeof(bool) && type != typeof(char)
            || type == typeof(decimal);
    }

    private static bool TryToDouble(object? value, out double number)
    {
        number = 0;

        if (value is null)
            return false;

        switch (value)
        {
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;

            default:
                return false;
        }
    }

    private static bool EnsureInteractive()
    {
        if (!Console.IsOutputRedirected)
            return true;

        CliUi.WriteWarning("Charts require an interactive terminal.");
        return false;
    }
}
