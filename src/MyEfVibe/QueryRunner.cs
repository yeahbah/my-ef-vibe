namespace MyEfVibe;

internal static class QueryRunner
{
    internal static async Task<int> RunOnceAsync(
        object dbContextInstance,
        ScriptSession session,
        WorkspaceHost host,
        DbLogSettings dbLogSettings,
        string expression,
        CancellationToken cancellationToken = default)
    {
        var analytics = new SessionAnalytics();

        try
        {
            await QueryResultWriter.WriteEvaluationAsync(
                dbContextInstance,
                session,
                expression,
                dbLogSettings,
                host,
                analytics,
                cancellationToken: cancellationToken);

            return 0;
        }
        catch (EvaluationFailedException evaluationFailure)
        {
            AnalyticsPresenter.WriteFooter(evaluationFailure.Metrics);
            CliUi.WriteErrorPanel("Evaluation error", evaluationFailure.Message);

            return 20;
        }
        catch (CompilationEvaluationException compilationFailure)
        {
            CliUi.WriteErrorPanel("Compilation error", compilationFailure.Message);

            return 20;
        }
        catch (Exception failure)
        {
            CliUi.WriteErrorPanel("Query failed", failure.ToString());

            return 21;
        }
    }
}
