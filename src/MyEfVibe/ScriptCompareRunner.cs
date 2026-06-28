using MyEfVibe.Reporters;
using MyEfVibe.Workspace;
using Spectre.Console;

namespace MyEfVibe;

internal sealed record ScriptCompareEvaluation(
    int Index,
    string Label,
    string Snippet,
    object? Result,
    EvaluationMetrics Metrics,
    string? Error);

internal static class ScriptCompareRunner
{
    internal static async Task RunCompareAsync(
        object dbContextInstance,
        ScriptSession session,
        DbLogSettings dbLogSettings,
        WorkspaceHost host,
        SessionAnalytics analytics,
        IReadOnlyList<ScriptAttributedBlock> compareBlocks,
        CliOutputFormat outputFormat = CliOutputFormat.Text,
        bool withPlan = false,
        CancellationToken cancellationToken = default)
    {
        host.EnsureEntityFrameworkRelationalLoaded();
        host.EnsureAspNetCoreSharedFrameworkLoaded();

        var evaluations = new List<ScriptCompareEvaluation>(compareBlocks.Count);

        for (var index = 0; index < compareBlocks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var block = compareBlocks[index];
            var label = ResolveCompareLabel(block, index);

            try
            {
                var (result, variantMetrics) = await QueryEvaluator.EvaluateAsync(
                    dbContextInstance,
                    session,
                    block.Code,
                    dbLogSettings,
                    host.EnumerateLoadedAssemblies(),
                    cancellationToken);

                evaluations.Add(new ScriptCompareEvaluation(index + 1, label, block.Code, result, variantMetrics, null));
            }
            catch (EvaluationFailedException failure)
            {
                evaluations.Add(
                    new ScriptCompareEvaluation(
                        index + 1,
                        label,
                        block.Code,
                        null,
                        failure.Metrics,
                        failure.Message));
            }
        }

        var compareMetrics = evaluations.Select(static entry => entry.Metrics).ToArray();
        var compareLabels = evaluations.Select(static entry => entry.Label).ToArray();
        analytics.SetCompareGroup(compareMetrics, compareLabels);

        var lastSuccessful = evaluations.LastOrDefault(static entry => entry.Metrics.Succeeded);

        if (lastSuccessful is not null)
        {
            var (_, _, _, _, _, exportRows) = ResultAnalyzer.Analyze(lastSuccessful.Result);
            analytics.Record(lastSuccessful.Metrics, lastSuccessful.Result, exportRows);
        }
        else if (evaluations.Count > 0)
        {
            analytics.Record(evaluations[^1].Metrics, null, []);
        }

        if (outputFormat == CliOutputFormat.Json)
        {
            var compareEntries = evaluations
                .Select(static entry => EvaluationJsonReporter.BuildCompareEntry(
                    entry.Index,
                    entry.Label,
                    entry.Snippet,
                    entry.Metrics,
                    entry.Error))
                .ToArray();

            if (lastSuccessful is not null)
            {
                QueryPlanResult? planResult = null;

                if (withPlan)
                {
                    planResult = await QueryPlanRunner.TryExplainAsync(
                        dbContextInstance,
                        AnalyticsPresenter.GetPlanSql(lastSuccessful.Metrics),
                        host.EnumerateLoadedAssemblies(),
                        host.ActiveProviderDescriptor,
                        cancellationToken);
                }

                EvaluationJsonReporter.WriteCompareSuccess(
                    lastSuccessful.Result,
                    lastSuccessful.Metrics,
                    compareEntries,
                    planResult);
            }
            else
            {
                EvaluationJsonReporter.WriteCompareFailure(evaluations[^1].Metrics, compareEntries);
            }

            return;
        }

        VisualizationPresenter.WriteCompareGroup(compareMetrics, compareLabels);

        var table = new Table().RoundedBorder().BorderColor(Color.Grey);
        table.AddColumn("variant");
        table.AddColumn("total ms");
        table.AddColumn("db ms");
        table.AddColumn("rows");
        table.AddColumn("sql");
        table.AddColumn("status");

        foreach (var entry in evaluations)
        {
            table.AddRow(
                entry.Label,
                entry.Metrics.TotalMilliseconds.ToString(),
                entry.Metrics.DatabaseMilliseconds?.ToString() ?? "—",
                entry.Metrics.RowCount?.ToString() ?? "—",
                entry.Metrics.SqlCommandCount.ToString(),
                entry.Metrics.Succeeded ? "ok" : entry.Error ?? "failed");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static string ResolveCompareLabel(ScriptAttributedBlock block, int index)
    {
        return string.IsNullOrWhiteSpace(block.Parameter) ? $"#{index + 1}" : block.Parameter;
    }
}
