namespace MyEfVibe;

internal static class QueryResultWriter
{
    internal static async Task WriteEvaluationAsync(
        object dbContextInstance,
        ScriptSession session,
        string snippet,
        SqlDisplaySettings sqlSettings,
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
                sqlSettings,
                host,
                cancellationToken);

            var (_, _, _, _, _, exportRows) = ResultAnalyzer.Analyze(result);

            analytics.Record(metrics, result, exportRows);
            AnalyticsPresenter.WriteEvaluation(result, metrics, sqlSettings, useSpectre);
        }
        catch (EvaluationFailedException failure)
        {
            analytics.Record(failure.Metrics, null, Array.Empty<object?>());
            throw;
        }
    }
}
