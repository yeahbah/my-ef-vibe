using MyEfVibe.Reporters;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal static class ServeEvaluator
{
    internal static async Task EvaluateAndWriteJsonAsync(
        WorkspaceRuntime runtime,
        string expression,
        bool withPlan,
        CancellationToken cancellationToken = default)
    {
        runtime.Host.EnsureEntityFrameworkRelationalLoaded();
        runtime.Host.EnsureAspNetCoreSharedFrameworkLoaded();

        if (ScriptAttributeParser.TryGetCompareBlocks(expression, out var compareBlocks))
        {
            try
            {
                await ScriptCompareRunner.RunCompareAsync(
                    runtime.DbContext,
                    runtime.Session,
                    runtime.DbLogSettings,
                    runtime.Host,
                    runtime.Analytics,
                    compareBlocks,
                    CliOutputFormat.Json,
                    withPlan,
                    cancellationToken);
            }
            catch (EvaluationFailedException evaluationFailure)
            {
                runtime.Analytics.Record(evaluationFailure.Metrics, null, []);
                EvaluationJsonReporter.WriteFailure(evaluationFailure.Metrics, evaluationFailure.Message);
            }

            return;
        }

        if (ScriptAttributeParser.TryGetBenchmarkBlock(expression, out var benchmarkBlock)
            && benchmarkBlock is not null)
        {
            try
            {
                var iterations = ScriptAttributeParser.GetBenchmarkIterations(benchmarkBlock);

                await ScriptBenchmarkRunner.RunBenchmarkAsync(
                    runtime.DbContext,
                    runtime.Session,
                    runtime.DbLogSettings,
                    runtime.Host,
                    runtime.Analytics,
                    benchmarkBlock,
                    iterations,
                    CliOutputFormat.Json,
                    withPlan,
                    cancellationToken);
            }
            catch (EvaluationFailedException evaluationFailure)
            {
                runtime.Analytics.Record(evaluationFailure.Metrics, null, []);
                EvaluationJsonReporter.WriteFailure(evaluationFailure.Metrics, evaluationFailure.Message);
            }

            return;
        }

        try
        {
            var (result, metrics) = await QueryEvaluator.EvaluateAsync(
                runtime.DbContext,
                runtime.Session,
                expression,
                runtime.DbLogSettings,
                runtime.Host.EnumerateLoadedAssemblies(),
                cancellationToken);

            var (_, _, _, _, _, exportRows) = ResultAnalyzer.Analyze(result);
            runtime.Analytics.Record(metrics, result, exportRows);

            QueryPlanResult? planResult = null;

            if (withPlan)
            {
                planResult = await QueryPlanRunner.TryExplainAsync(
                    runtime.DbContext,
                    AnalyticsPresenter.GetPlanSql(metrics),
                    runtime.Host.EnumerateLoadedAssemblies(),
                    runtime.Host.ActiveProviderDescriptor,
                    cancellationToken);
            }

            EvaluationJsonReporter.WriteSuccess(result, metrics, planResult);
        }
        catch (EvaluationFailedException evaluationFailure)
        {
            runtime.Analytics.Record(evaluationFailure.Metrics, null, []);
            EvaluationJsonReporter.WriteFailure(evaluationFailure.Metrics, evaluationFailure.Message);
        }
        catch (CompilationEvaluationException compilationFailure)
        {
            EvaluationJsonReporter.WriteFailure(
                EvaluationMetrics.Failed(expression, 0, compilationFailure.Message),
                compilationFailure.Message);
        }
        catch (Exception failure)
        {
            EvaluationJsonReporter.WriteFailure(
                EvaluationMetrics.Failed(expression, 0, failure.Message),
                failure.Message);
        }
    }
}