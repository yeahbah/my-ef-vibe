namespace MyEfVibe.IntegrationTests;

internal static class IntegrationTestGuards
{
    internal static void RequireEnabled()
    {
        Skip.IfNot(
            IntegrationEnvironment.IsEnabled,
            $"Set {IntegrationEnvironment.EnableVariableName}=1 to run local integration tests.");
    }

    internal static async Task<EfvibeIntegrationSession> RequireSessionAsync(
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        RequireEnabled();

        var scenario = IntegrationScenarioCatalog.Require(scenarioId);

        Skip.IfNot(
            DatabaseProbe.TryValidateScenario(scenario, out var validationFailure),
            validationFailure ?? "Scenario paths are invalid.");

        try
        {
            return await EfvibeIntegrationSession.ConnectAsync(scenario, cancellationToken);
        }
        catch (Exception failure)
        {
            Skip.If(true, failure.Message);
            throw;
        }
    }
}