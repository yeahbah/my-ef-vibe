namespace MyEfVibe.IntegrationTests;

public sealed class SqliteConnectivityTests
{
    [SkippableFact]
    public async Task Sqlite_database_is_reachable_with_prebuilt_workspace()
    {
        IntegrationTestGuards.RequireEnabled();

        var persistenceDll = FindPrebuiltDll("AdventureWorks.Infrastructure.Persistence.dll");
        var startupDll = FindPrebuiltDll("AdventureWorks.API.dll");

        Skip.If(persistenceDll is null || startupDll is null,
            "No pre-built AdventureWorks output under /tmp/efvibe-integration.");

        var scenario = IntegrationScenarioCatalog.Require("sqlite");
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

        Assert.Contains("Sqlite", DatabaseProbe.ReadProviderName(dbContext), StringComparison.OrdinalIgnoreCase);
        Assert.True(await DatabaseProbe.CanConnectAsync(dbContext, scriptSession, host));
    }

    [SkippableFact]
    public async Task Sqlite_products_query_materializes()
    {
        IntegrationTestGuards.RequireEnabled();

        var persistenceDll = FindPrebuiltDll("AdventureWorks.Infrastructure.Persistence.dll");
        var startupDll = FindPrebuiltDll("AdventureWorks.API.dll");

        Skip.If(persistenceDll is null || startupDll is null,
            "No pre-built AdventureWorks output under /tmp/efvibe-integration.");
        Skip.If(!File.Exists("/home/yeahbah/Projects/AdventureWorksSqlite/database/sqlite/AdventureWorks.db"),
            "SQLite database not found.");

        var scenario = IntegrationScenarioCatalog.Require("sqlite");
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

        var sql = metrics.ExecutedSql.Count > 0
            ? string.Join(Environment.NewLine, metrics.ExecutedSql)
            : metrics.TranslatedSql;

        Assert.False(string.IsNullOrWhiteSpace(sql));
        Assert.Contains("Production.Product", sql, StringComparison.Ordinal);
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