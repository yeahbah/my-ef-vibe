using MyEfVibe.Reporters;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal static class QueryResultWriter
{
    internal static async Task WriteEvaluationAsync(
        object dbContextInstance,
        ScriptSession session,
        string snippet,
        DbLogSettings dbLogSettings,
        WorkspaceHost host,
        SessionAnalytics analytics,
        CliOutputFormat outputFormat = CliOutputFormat.Text,
        bool withPlan = false,
        TextWriter? output = null,
        CancellationToken cancellationToken = default)
    {
        var useSpectre = outputFormat == CliOutputFormat.Text
                         && (output is null || output == Console.Out);

        host.EnsureEntityFrameworkRelationalLoaded();
        host.EnsureAspNetCoreSharedFrameworkLoaded();

        if (ScriptAttributeParser.TryGetCompareBlocks(snippet, out var compareBlocks))
        {
            await ScriptCompareRunner.RunCompareAsync(
                dbContextInstance,
                session,
                dbLogSettings,
                host,
                analytics,
                compareBlocks,
                outputFormat,
                withPlan,
                cancellationToken);

            return;
        }

        if (ScriptAttributeParser.TryGetBenchmarkBlock(snippet, out var benchmarkBlock)
            && benchmarkBlock is not null)
        {
            var iterations = ScriptAttributeParser.GetBenchmarkIterations(benchmarkBlock);

            await ScriptBenchmarkRunner.RunBenchmarkAsync(
                dbContextInstance,
                session,
                dbLogSettings,
                host,
                analytics,
                benchmarkBlock,
                iterations,
                outputFormat,
                withPlan,
                cancellationToken);

            return;
        }

        try
        {
            var (result, metrics) = await QueryEvaluator.EvaluateAsync(
                dbContextInstance,
                session,
                snippet,
                dbLogSettings,
                host.EnumerateLoadedAssemblies(),
                cancellationToken);

            var (_, _, _, _, _, exportRows) = ResultAnalyzer.Analyze(result);

            analytics.Record(metrics, result, exportRows);
            if (outputFormat == CliOutputFormat.Json)
            {
                QueryPlanResult? planResult = null;

                if (withPlan)
                {
                    planResult = await QueryPlanRunner.TryExplainAsync(
                        dbContextInstance,
                        AnalyticsPresenter.GetPlanSql(metrics),
                        host.EnumerateLoadedAssemblies(),
                        host.ActiveProviderDescriptor,
                        cancellationToken);
                }

                EvaluationJsonReporter.WriteSuccess(result, metrics, planResult);
                return;
            }

            AnalyticsPresenter.WriteEvaluation(result, metrics, dbLogSettings, useSpectre);
        }
        catch (EvaluationFailedException failure)
        {
            analytics.Record(failure.Metrics, null, []);
            throw;
        }
    }
}