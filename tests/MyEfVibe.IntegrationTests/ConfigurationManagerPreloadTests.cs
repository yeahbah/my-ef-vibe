namespace MyEfVibe.IntegrationTests;

public sealed class ConfigurationManagerPreloadTests
{
    [SkippableFact]
    public void WorkspaceHost_loads_with_startup_merge_and_npgsql_override()
    {
        IntegrationTestGuards.RequireEnabled();

        var persistenceDll = FindPrebuiltDll("AdventureWorks.Infrastructure.Persistence.dll");
        var startupDll = FindPrebuiltDll("AdventureWorks.API.dll");

        Skip.If(persistenceDll is null || startupDll is null, "No pre-built AdventureWorks output under /tmp/efvibe-integration.");

        var scenario = IntegrationScenarioCatalog.Require("postgresql");
        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        var startupOutputDirectory = Path.GetDirectoryName(startupDll)!;

        var workspaceBuild = new WorkspaceBuildResult(
            SessionDirectory: Path.Combine(Path.GetTempPath(), "efvibe-integration-smoke", Guid.NewGuid().ToString("N")),
            ProjectPath: scenario.EfProjectPath,
            StartupProjectPath: scenario.StartupProjectPath,
            OutputDirectory: outputDirectory,
            PrimaryAssemblyDll: persistenceDll,
            TargetFrameworkMoniker: scenario.Framework,
            ProjectBuildOutput: new ProjectBuildOutput(outputDirectory),
            StartupOutputDirectory: startupOutputDirectory);

        using var host = WorkspaceHost.Load(workspaceBuild);

        var dbContext = DbContextActivator.ResolveInstance(
            host,
            scenario.Context,
            scenario.ConnectionString,
            ProviderParser.ParseOrNull(scenario.Provider),
            allowInteractiveSelection: false);

        Assert.Equal(scenario.Context, dbContext.GetType().Name);
    }

    private static string? FindPrebuiltDll(string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-integration");

        if (!Directory.Exists(root))
            return null;

        return Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
