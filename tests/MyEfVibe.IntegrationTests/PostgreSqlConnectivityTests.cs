namespace MyEfVibe.IntegrationTests;

public sealed class PostgreSqlConnectivityTests
{
    [SkippableFact]
    public async Task PostgreSql_products_query_materializes()
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
        Assert.Contains("production", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("product", sql, StringComparison.OrdinalIgnoreCase);
    }
}