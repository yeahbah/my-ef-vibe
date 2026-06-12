namespace MyEfVibe.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public sealed class MultiContextIntegrationTests(IntegrationSessionCache sessions)
{
    private readonly IntegrationSessionCache _sessions = sessions;

    public static TheoryData<string> MultiContextScenarioIds =>
        new("multicontext-postgresql", "multicontext-oracle");

    [SkippableTheory]
    [MemberData(nameof(MultiContextScenarioIds))]
    public async Task Database_is_reachable(string scenarioId)
    {
        var session = await _sessions.GetAsync(scenarioId);

        Assert.True(DatabaseProbe.ProviderMatches(session.Scenario, DatabaseProbe.ReadProviderName(session.DbContext)));
        Assert.True(await DatabaseProbe.CanConnectAsync(session.DbContext, session.ScriptSession, session.Host));
    }

    [SkippableTheory]
    [MemberData(nameof(MultiContextScenarioIds))]
    public async Task Products_query_materializes(string scenarioId)
    {
        var session = await _sessions.GetAsync(scenarioId);

        var (_, metrics) = await QueryEvaluator.EvaluateAsync(
            session.DbContext,
            session.ScriptSession,
            "db.Products.Take(3).ToList();",
            new DbLogSettings { Enabled = true },
            session.Host.EnumerateLoadedAssemblies());

        Assert.True(metrics.Succeeded);
        Assert.Equal(3, metrics.RowCount);

        var sql = metrics.ExecutedSql.Count > 0
            ? string.Join(Environment.NewLine, metrics.ExecutedSql)
            : metrics.TranslatedSql;

        Assert.False(string.IsNullOrWhiteSpace(sql));
        Assert.Contains("product", sql, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableTheory]
    [MemberData(nameof(MultiContextScenarioIds))]
    public async Task Tables_json_lists_products(string scenarioId)
    {
        var session = await _sessions.GetAsync(scenarioId);

        var tables = SchemaBrowser.GetDbSets(session.DbContext);

        Assert.Single(tables);
        Assert.Equal("Products", tables[0].DbSet, StringComparer.Ordinal);
    }

    [SkippableFact]
    public async Task Same_ef_project_exposes_both_context_types()
    {
        IntegrationTestGuards.RequireEnabled();

        var postgres = IntegrationScenarioCatalog.Require("multicontext-postgresql");
        var oracle = IntegrationScenarioCatalog.Require("multicontext-oracle");

        Skip.IfNot(
            DatabaseProbe.TryValidateScenario(postgres, out var validationFailure),
            validationFailure ?? "multicontext-postgresql paths are invalid.");

        Assert.Equal(postgres.EfProjectPath, oracle.EfProjectPath, StringComparer.Ordinal);
        Assert.NotEqual(postgres.Context, oracle.Context, StringComparer.Ordinal);

        var postgresSession = await _sessions.GetAsync("multicontext-postgresql");
        var oracleSession = await _sessions.GetAsync("multicontext-oracle");

        Assert.Equal("PostgresAdventureWorksDbContext", postgresSession.DbContext.GetType().Name, StringComparer.Ordinal);
        Assert.Equal("OracleAdventureWorksDbContext", oracleSession.DbContext.GetType().Name, StringComparer.Ordinal);
    }
}
