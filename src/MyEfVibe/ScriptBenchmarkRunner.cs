using MyEfVibe.Reporters;
using MyEfVibe.Workspace;
using Spectre.Console;

namespace MyEfVibe;

internal sealed record ScriptBenchmarkSample(int Iteration, EvaluationMetrics Metrics, string? Error);

internal static class ScriptBenchmarkRunner
{
    internal static async Task RunBenchmarkAsync(
        object dbContextInstance,
        ScriptSession session,
        DbLogSettings dbLogSettings,
        WorkspaceHost host,
        SessionAnalytics analytics,
        ScriptAttributedBlock benchmarkBlock,
        int iterations,
        CliOutputFormat outputFormat = CliOutputFormat.Text,
        bool withPlan = false,
        CancellationToken cancellationToken = default)
    {
        host.EnsureEntityFrameworkRelationalLoaded();
        host.EnsureAspNetCoreSharedFrameworkLoaded();

        if (iterations < 1)
        {
            var message = "Benchmark iteration count must be at least 1.";
            if (outputFormat == CliOutputFormat.Json)
            {
                EvaluationJsonReporter.WriteFailure(
                    EvaluationMetrics.Failed(benchmarkBlock.Code, 0, message),
                    message);
            }
            else
            {
                CliUi.WriteWarning(message);
            }

            return;
        }

        var samples = new List<ScriptBenchmarkSample>(iterations);
        object? lastResult = null;
        EvaluationMetrics? lastMetrics = null;

        for (var iteration = 1; iteration <= iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var (result, metrics) = await QueryEvaluator.EvaluateAsync(
                    dbContextInstance,
                    session,
                    benchmarkBlock.Code,
                    dbLogSettings,
                    host.EnumerateLoadedAssemblies(),
                    cancellationToken);

                samples.Add(new ScriptBenchmarkSample(iteration, metrics, null));
                lastResult = result;
                lastMetrics = metrics;
            }
            catch (EvaluationFailedException failure)
            {
                samples.Add(new ScriptBenchmarkSample(iteration, failure.Metrics, failure.Message));

                if (outputFormat == CliOutputFormat.Json)
                {
                    analytics.Record(failure.Metrics, null, []);
                    EvaluationJsonReporter.WriteBenchmarkFailure(
                        failure.Metrics,
                        benchmarkBlock.Code,
                        iterations,
                        BuildJsonSamples(samples),
                        failure.Message);
                }
                else
                {
                    CliUi.WriteError(failure.Message);
                }

                return;
            }
        }

        if (lastMetrics is null)
        {
            return;
        }

        var (_, _, _, _, _, exportRows) = ResultAnalyzer.Analyze(lastResult);
        analytics.Record(lastMetrics, lastResult, exportRows);

        var benchmarkResult = EvaluationJsonReporter.BuildBenchmarkResult(
            benchmarkBlock.Code,
            iterations,
            BuildJsonSamples(samples));

        if (outputFormat == CliOutputFormat.Json)
        {
            QueryPlanResult? planResult = null;

            if (withPlan)
            {
                planResult = await QueryPlanRunner.TryExplainAsync(
                    dbContextInstance,
                    AnalyticsPresenter.GetPlanSql(lastMetrics),
                    host.EnumerateLoadedAssemblies(),
                    host.ActiveProviderDescriptor,
                    cancellationToken);
            }

            EvaluationJsonReporter.WriteBenchmarkSuccess(lastResult, lastMetrics, benchmarkResult, planResult);
            return;
        }

        WriteTextSummary(iterations, samples);
        VisualizationPresenter.WriteBenchmarkTimings(samples.Select(static sample => sample.Metrics.TotalMilliseconds).ToArray());
    }

    private static void WriteTextSummary(int iterations, IReadOnlyList<ScriptBenchmarkSample> samples)
    {
        var timings = samples.Select(static sample => sample.Metrics.TotalMilliseconds).ToArray();

        AnsiConsole.MarkupLine($"[grey]Benchmarked {iterations} iteration(s)[/]");

        var table = new Table().RoundedBorder().BorderColor(Color.Grey);
        table.AddColumn("iteration");
        table.AddColumn("total ms");
        table.AddColumn("db ms");
        table.AddColumn("rows");
        table.AddColumn("sql");

        foreach (var sample in samples)
        {
            table.AddRow(
                sample.Iteration.ToString(),
                sample.Metrics.TotalMilliseconds.ToString(),
                sample.Metrics.DatabaseMilliseconds?.ToString() ?? "—",
                sample.Metrics.RowCount?.ToString() ?? "—",
                sample.Metrics.SqlCommandCount.ToString());
        }

        AnsiConsole.Write(table);

        var stats = new Table().RoundedBorder().BorderColor(Color.Grey);
        stats.AddColumn("metric");
        stats.AddColumn("ms");
        stats.AddRow("min", timings.Min().ToString());
        stats.AddRow("avg", timings.Average().ToString("F1"));
        stats.AddRow("max", timings.Max().ToString());
        stats.AddRow("p95", Percentile(timings, 0.95).ToString("F1"));

        AnsiConsole.Write(stats);
        AnsiConsole.WriteLine();
    }

    private static IReadOnlyList<EvaluationJsonReporter.EvaluationJsonBenchmarkSample> BuildJsonSamples(
        IReadOnlyList<ScriptBenchmarkSample> samples)
    {
        return samples
            .Select(static sample => new EvaluationJsonReporter.EvaluationJsonBenchmarkSample
            {
                Iteration = sample.Iteration,
                TotalMs = sample.Metrics.TotalMilliseconds,
                DatabaseMs = sample.Metrics.DatabaseMilliseconds,
                RowCount = sample.Metrics.RowCount,
                SqlCommandCount = sample.Metrics.SqlCommandCount,
                Success = sample.Metrics.Succeeded,
                Error = sample.Error,
            })
            .ToArray();
    }

    private static double Percentile(IReadOnlyList<long> values, double percentile)
    {
        var ordered = values.OrderBy(static value => value).ToArray();
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;

        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }
}
