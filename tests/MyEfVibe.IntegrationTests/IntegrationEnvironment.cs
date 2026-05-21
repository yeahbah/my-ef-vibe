namespace MyEfVibe.IntegrationTests;

internal static class IntegrationEnvironment
{
    internal const string EnableVariableName = "EFVIBE_RUN_INTEGRATION";

    internal static bool IsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnableVariableName),
            "1",
            StringComparison.Ordinal);
}
