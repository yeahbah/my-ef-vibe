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
            runtime.Analytics.Record(evaluationFailure.Metrics, null, Array.Empty<object?>());
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