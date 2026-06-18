using MyEfVibe.Reporters;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal static class QueryRunner
{
    internal static async Task<int> RunOnceAsync(
        object dbContextInstance,
        ScriptSession session,
        WorkspaceHost host,
        DbLogSettings dbLogSettings,
        string expression,
        CliOutputFormat outputFormat = CliOutputFormat.Text,
        bool withPlan = false,
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
                outputFormat,
                withPlan,
                cancellationToken: cancellationToken);

            return 0;
        }
        catch (EvaluationFailedException evaluationFailure)
        {
            if (outputFormat == CliOutputFormat.Json)
            {
                EvaluationJsonReporter.WriteFailure(evaluationFailure.Metrics, evaluationFailure.Message);
                return 20;
            }

            AnalyticsPresenter.WriteFooter(evaluationFailure.Metrics);
            CliUi.WriteErrorPanel("Evaluation error", evaluationFailure.Message);

            return 20;
        }
        catch (CompilationEvaluationException compilationFailure)
        {
            if (outputFormat == CliOutputFormat.Json)
            {
                EvaluationJsonReporter.WriteFailure(
                    EvaluationMetrics.Failed(expression, 0, compilationFailure.Message),
                    compilationFailure.Message);

                return 20;
            }

            CliUi.WriteErrorPanel("Compilation error", compilationFailure.Message);

            return 20;
        }
        catch (Exception failure)
        {
            if (outputFormat == CliOutputFormat.Json)
            {
                EvaluationJsonReporter.WriteFailure(
                    EvaluationMetrics.Failed(expression, 0, failure.Message),
                    failure.Message);

                return 21;
            }

            CliUi.WriteErrorPanel("Query failed", failure.ToString());

            return 21;
        }
    }
}