using System.Diagnostics;
using Spectre.Console;

namespace MyEfVibe;

internal static class BenchmarkRunner
{
    internal static async Task RunAsync(
        object dbContext,
        ScriptSession session,
        string snippet,
        int iterations,
        CancellationToken cancellationToken = default)
    {
        if (iterations < 1)
        {
            CliUi.WriteWarning("Iteration count must be at least 1.");
            return;
        }

        var timings = new List<long>();

        AnsiConsole.MarkupLine($"[grey]Benchmarking {iterations} iteration(s)…[/]");

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();

            await session.EvaluateAsync(snippet, cancellationToken);

            stopwatch.Stop();
            timings.Add(stopwatch.ElapsedMilliseconds);
        }

        var table = new Table().RoundedBorder().BorderColor(Color.Grey);
        table.AddColumn("metric");
        table.AddColumn("ms");

        table.AddRow("min", timings.Min().ToString());
        table.AddRow("avg", timings.Average().ToString("F1"));
        table.AddRow("max", timings.Max().ToString());
        table.AddRow("p95", Percentile(timings, 0.95).ToString("F1"));

        AnsiConsole.Write(table);
        VisualizationPresenter.WriteBenchmarkTimings(timings);
    }

    private static double Percentile(IReadOnlyList<long> values, double percentile)
    {
        var ordered = values.OrderBy(static value => value).ToArray();
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;

        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }
}
