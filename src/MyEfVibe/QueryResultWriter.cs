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
        TextWriter? output = null,
        CancellationToken cancellationToken = default)
    {
        var useSpectre = output is null || output == Console.Out;

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
            AnalyticsPresenter.WriteEvaluation(result, metrics, dbLogSettings, useSpectre);
        }
        catch (EvaluationFailedException failure)
        {
            analytics.Record(failure.Metrics, null, Array.Empty<object?>());
            throw;
        }
    }
}
