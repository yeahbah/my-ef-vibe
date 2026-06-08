namespace MyEfVibe.IntegrationTests;

public sealed class OracleConnectivityTests
{
    [SkippableFact]
    public async Task Oracle_database_is_reachable_with_prebuilt_workspace()
    {
        IntegrationTestGuards.RequireEnabled();

        var persistenceDll = FindPrebuiltDll("AdventureWorks.Infrastructure.Persistence.dll");
        var startupDll = FindPrebuiltDll("AdventureWorks.API.dll");

        Skip.If(persistenceDll is null || startupDll is null,
            "No pre-built AdventureWorks output under /tmp/efvibe-integration.");

        var scenario = IntegrationScenarioCatalog.Require("oracle");
        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        var startupOutputDirectory = Path.GetDirectoryName(startupDll)!;

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

        var scriptSession = new ScriptSession(
            dbContext.GetType(),
            dbContext,
            workspaceBuild.ReferenceAssemblyPaths,
            host.AssemblyLoader);

        var providerName = DatabaseProbe.ReadProviderName(dbContext);
        Assert.Contains("Oracle", providerName, StringComparison.OrdinalIgnoreCase);

        Assert.True(await DatabaseProbe.CanConnectAsync(dbContext, scriptSession, host));
    }

    [SkippableFact]
    public async Task Oracle_products_query_materializes()
    {
        IntegrationTestGuards.RequireEnabled();

        var persistenceDll = FindPrebuiltDll("AdventureWorks.Infrastructure.Persistence.dll");
        var startupDll = FindPrebuiltDll("AdventureWorks.API.dll");

        Skip.If(persistenceDll is null || startupDll is null,
            "No pre-built AdventureWorks output under /tmp/efvibe-integration.");

        var scenario = IntegrationScenarioCatalog.Require("oracle");
        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        var startupOutputDirectory = Path.GetDirectoryName(startupDll)!;

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

        var scriptSession = new ScriptSession(
            dbContext.GetType(),
            dbContext,
            workspaceBuild.ReferenceAssemblyPaths,
            host.AssemblyLoader);

        var (_, metrics) = await QueryEvaluator.EvaluateAsync(
            dbContext,
            scriptSession,
            "db.Products.Take(3).ToList();",
            new DbLogSettings { Enabled = true },
            host.EnumerateLoadedAssemblies());

        Assert.True(metrics.Succeeded);
        Assert.True(metrics.RowCount >= 1);
    }

    private static string? FindPrebuiltDll(string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-integration");

        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));
    }
}