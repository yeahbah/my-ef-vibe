namespace MyEfVibe.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public sealed class AdventureWorksIntegrationTests(IntegrationSessionCache sessions)
{
    private readonly IntegrationSessionCache _sessions = sessions;

    public static TheoryData<string> ScenarioIds =>
        new(IntegrationScenarioCatalog.Load().Select(static scenario => scenario.Id));

    [SkippableTheory]
    [MemberData(nameof(ScenarioIds))]
    public async Task Database_is_reachable(string scenarioId)
    {
        var session = await _sessions.GetAsync(scenarioId);

        Assert.True(DatabaseProbe.ProviderMatches(session.Scenario, DatabaseProbe.ReadProviderName(session.DbContext)));
    }

    [SkippableTheory]
    [MemberData(nameof(ScenarioIds))]
    public async Task Query_products_materializes_with_sql(string scenarioId)
    {
        var session = await _sessions.GetAsync(scenarioId);

        var (_, metrics) = await QueryEvaluator.EvaluateAsync(
            session.DbContext,
            session.ScriptSession,
            "db.Products.Take(3).ToList();",
            new DbLogSettings { Enabled = true },
            session.Host.EnumerateLoadedAssemblies());

        Assert.True(metrics.Succeeded);
        Assert.True(metrics.RowCount >= 1);
        Assert.True(metrics.SqlCommandCount > 0 || !string.IsNullOrWhiteSpace(metrics.TranslatedSql));
    }

    [SkippableTheory]
    [MemberData(nameof(ScenarioIds))]
    public async Task Tables_includes_products(string scenarioId)
    {
        var session = await _sessions.GetAsync(scenarioId);

        var tables = await SchemaBrowser.GetDbSetCountsAsync(session.DbContext);

        Assert.Contains(tables, entry => string.Equals(entry.DbSet, "Products", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tables, entry => entry.Count is > 0);
    }

    [SkippableTheory]
    [MemberData(nameof(ScenarioIds))]
    public async Task Query_plan_returns_rows(string scenarioId)
    {
        var session = await _sessions.GetAsync(scenarioId);

        var (_, metrics) = await QueryEvaluator.EvaluateAsync(
            session.DbContext,
            session.ScriptSession,
            "db.Products.Take(1).ToList();",
            new DbLogSettings { Enabled = true },
            session.Host.EnumerateLoadedAssemblies());

        Assert.True(metrics.Succeeded);

        var sql = metrics.ExecutedSql.Count > 0
            ? string.Join(Environment.NewLine, metrics.ExecutedSql)
            : metrics.TranslatedSql;

        Assert.False(string.IsNullOrWhiteSpace(sql));

        var plan = await QueryPlanRunner.TryExplainAsync(
            session.DbContext,
            sql,
            session.Host.EnumerateLoadedAssemblies());

        Assert.Null(plan.Note);
        Assert.False(string.IsNullOrWhiteSpace(plan.PlanText));
    }

    [SkippableTheory]
    [MemberData(nameof(ScenarioIds))]
    public void Scan_lite_completes(string scenarioId)
    {
        IntegrationTestGuards.RequireEnabled();

        var scenario = IntegrationScenarioCatalog.Require(scenarioId);

        Skip.IfNot(
            DatabaseProbe.TryValidateScenario(scenario, out var validationFailure),
            validationFailure ?? "Scenario paths are invalid.");

        var result = LinqLiteScanner.Scan(scenario.EfProjectPath, scenario.StartupProjectPath);

        Assert.True(result.FilesScanned > 0);
        Assert.True(result.ProjectsScanned > 0);
    }

    [SkippableTheory]
    [MemberData(nameof(ScenarioIds))]
    public async Task Scan_deep_produces_translated_sql_and_plan(string scenarioId)
    {
        var session = await _sessions.GetAsync(scenarioId);

        var (scanResult, stats) = await LinqDeepScanner.ScanAsync(
            session.Scenario.EfProjectPath,
            session.Scenario.StartupProjectPath,
            session.ScriptSession,
            session.Host,
            session.DbContext.GetType());

        Assert.True(stats.QuerySitesVisited > 0);
        Assert.True(stats.SqlTranslatedCount > 0, "Expected at least one translated SQL site.");

        var withSql = scanResult.Findings
            .Where(finding => !string.IsNullOrWhiteSpace(finding.TranslatedSql))
            .ToArray();

        Assert.NotEmpty(withSql);

        Assert.True(
            stats.QueryPlanCount > 0,
            $"Expected at least one EXPLAIN plan (translated={stats.SqlTranslatedCount}, planFailed={stats.QueryPlanFailedCount}).");
    }
}
