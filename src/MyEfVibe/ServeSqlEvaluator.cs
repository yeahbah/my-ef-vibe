using MyEfVibe.Reporters;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal static class ServeSqlEvaluator
{
    internal static async Task ExecuteAndWriteJsonAsync(
        WorkspaceRuntime runtime,
        string sql,
        bool withPlan,
        QueryPagingOptions? paging = null,
        CancellationToken cancellationToken = default)
    {
        runtime.Host.EnsureEntityFrameworkRelationalLoaded();
        runtime.Host.EnsureAspNetCoreSharedFrameworkLoaded();

        try
        {
            var (result, metrics, rows) = await RawSqlExecutor.ExecuteAsync(
                runtime.DbContext,
                sql,
                runtime.Host.EnumerateLoadedAssemblies(),
                runtime.DbLogSettings,
                cancellationToken,
                paging);

            runtime.Analytics.Record(metrics, result, []);

            QueryPlanResult? planResult = null;

            if (withPlan && RawSqlClassifier.LooksLikeQuery(sql))
            {
                planResult = await QueryPlanRunner.TryExplainAsync(
                    runtime.DbContext,
                    metrics.ExecutedSql.FirstOrDefault() ?? sql.Trim(),
                    runtime.Host.EnumerateLoadedAssemblies(),
                    runtime.Host.ActiveProviderDescriptor,
                    cancellationToken);
            }

            EvaluationJsonReporter.WriteSqlSuccess(result, rows, metrics, planResult);
        }
        catch (EvaluationFailedException evaluationFailure)
        {
            runtime.Analytics.Record(evaluationFailure.Metrics, null, []);
            EvaluationJsonReporter.WriteFailure(evaluationFailure.Metrics, evaluationFailure.Message);
        }
        catch (Exception failure)
        {
            EvaluationJsonReporter.WriteFailure(
                EvaluationMetrics.Failed(sql, 0, failure.Message),
                failure.Message);
        }
    }
}
