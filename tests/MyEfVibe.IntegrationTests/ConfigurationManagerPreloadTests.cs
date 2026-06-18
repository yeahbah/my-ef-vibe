using MyEfVibe.Workspace;

namespace MyEfVibe.IntegrationTests;

public sealed class ConfigurationManagerPreloadTests
{
    [SkippableFact]
    public void WorkspaceHost_loads_with_startup_merge_and_npgsql_override()
    {
        IntegrationTestGuards.RequireEnabled();

        Skip.IfNot(
            IntegrationPrebuiltArtifacts.TryFindRelationalBuildOutputs(
                out var persistenceDll,
                out var startupDll,
                out var outputDirectory,
                out var startupOutputDirectory),
            "No pre-built AdventureWorks relational output under /tmp/efvibe-integration.");

        var scenario = IntegrationScenarioCatalog.Require("postgresql");

        var workspaceBuild = new WorkspaceBuildResult(
            Path.Combine(Path.GetTempPath(), "efvibe-integration-smoke", Guid.NewGuid().ToString("N")),
            scenario.EfProjectPath,
            scenario.StartupProjectPath,
            outputDirectory,
            persistenceDll,
            scenario.Framework,
            new ProjectBuildOutput(outputDirectory),
            StartupOutputDirectory: startupOutputDirectory);

        using var host = WorkspaceHost.Load(workspaceBuild);

        var dbContext = DbContextActivator.ResolveInstance(
            host,
            scenario.Context,
            scenario.ConnectionString,
            ProviderParser.ParseDescriptorOrNull(scenario.Provider),
            false);

        Assert.Equal(scenario.Context, dbContext.GetType().Name);
    }
}