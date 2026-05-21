using System.Text.Json;

namespace MyEfVibe.IntegrationTests;

internal static class IntegrationScenarioCatalog
{
    internal static IReadOnlyList<IntegrationScenario> Load()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "integration-scenarios.json");

        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Integration manifest not found: {manifestPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

        var root = document.RootElement;

        var integrationRoot = ResolveIntegrationRoot(
            root.TryGetProperty("integrationRoot", out var rootProperty)
                ? rootProperty.GetString()
                : null);

        if (!root.TryGetProperty("scenarios", out var scenariosProperty)
            || scenariosProperty.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("integration-scenarios.json must contain a scenarios array.");

        var scenarios = new List<IntegrationScenario>();

        foreach (var entry in scenariosProperty.EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString()
                       ?? throw new InvalidOperationException("Scenario id is required.");

            var provider = entry.GetProperty("provider").GetString()
                           ?? throw new InvalidOperationException($"Scenario {id} requires provider.");

            var repoRoot = entry.GetProperty("repoRoot").GetString()
                           ?? throw new InvalidOperationException($"Scenario {id} requires repoRoot.");

            var efProject = entry.GetProperty("efProject").GetString()
                            ?? throw new InvalidOperationException($"Scenario {id} requires efProject.");

            var startupProject = entry.GetProperty("startupProject").GetString()
                                 ?? throw new InvalidOperationException($"Scenario {id} requires startupProject.");

            var context = entry.GetProperty("context").GetString()
                          ?? throw new InvalidOperationException($"Scenario {id} requires context.");

            var framework = entry.TryGetProperty("framework", out var frameworkProperty)
                ? frameworkProperty.GetString()
                : null;

            var connectionString = entry.TryGetProperty("connectionString", out var connectionProperty)
                ? connectionProperty.GetString()
                : null;

            scenarios.Add(new IntegrationScenario(
                id,
                provider,
                Path.Combine(integrationRoot, repoRoot),
                efProject,
                startupProject,
                context,
                framework ?? "net10.0",
                connectionString));
        }

        return scenarios;
    }

    internal static IntegrationScenario Require(string id) =>
        Load().FirstOrDefault(scenario => string.Equals(scenario.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Unknown integration scenario `{id}`.");

    private static string ResolveIntegrationRoot(string? manifestRoot)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("EFVIBE_INTEGRATION_ROOT");

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return Path.GetFullPath(fromEnvironment.Trim());

        if (!string.IsNullOrWhiteSpace(manifestRoot))
            return Path.GetFullPath(manifestRoot.Trim());

        throw new InvalidOperationException(
            "Set EFVIBE_INTEGRATION_ROOT to the directory containing AdventureWorks sample repos.");
    }
}
