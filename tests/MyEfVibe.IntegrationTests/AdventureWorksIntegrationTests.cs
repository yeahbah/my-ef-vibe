using MyEfVibe.Linq;

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

        var (_, metrics) = await EvaluateOrAssertAsync(
            session,
            "db.Products.Take(3).ToList();");

        Assert.True(metrics.Succeeded);
        Assert.True(metrics.RowCount >= 1);

        if (session.Host.ActiveProviderDescriptor?.IsCouchbase != true)
        {
            Assert.True(metrics.SqlCommandCount > 0 || !string.IsNullOrWhiteSpace(metrics.TranslatedSql));
        }
    }

    [SkippableTheory]
    [MemberData(nameof(ScenarioIds))]
    public async Task Tables_includes_products(string scenarioId)
    {
        var session = await _sessions.GetAsync(scenarioId);

        var tables = SchemaBrowser.GetDbSets(session.DbContext);

        Assert.Contains(tables, entry => string.Equals(entry.DbSet, "Products", StringComparison.OrdinalIgnoreCase));
    }

    [SkippableTheory]
    [MemberData(nameof(ScenarioIds))]
    public async Task Query_plan_returns_rows(string scenarioId)
    {
        var session = await _sessions.GetAsync(scenarioId);

        Skip.IfNot(
            ProviderCapabilityResolver.SupportsQueryPlan(
                session.Host.ActiveProviderDescriptor,
                session.DbContext),
            "Provider does not support relational EXPLAIN plans.");

        var (_, metrics) = await EvaluateOrAssertAsync(
            session,
            "db.Products.Take(1).ToList();");

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

        Skip.If(
            session.Host.ActiveProviderDescriptor?.IsCouchbase == true,
            "Couchbase deep scan SQL++ translation is not available in this release.");

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

        if (ProviderCapabilityResolver.SupportsQueryPlan(
                session.Host.ActiveProviderDescriptor,
                session.DbContext))
        {
            var planFailureNotes = withSql
                .Where(finding => string.IsNullOrWhiteSpace(finding.QueryPlan)
                                  && !string.IsNullOrWhiteSpace(finding.QueryPlanNote))
                .Select(finding => finding.QueryPlanNote)
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToArray();

            Assert.True(
                stats.QueryPlanCount > 0,
                $"Expected at least one EXPLAIN plan (translated={stats.SqlTranslatedCount}, planFailed={stats.QueryPlanFailedCount})."
                + $" Sample plan failure(s): {string.Join(" | ", planFailureNotes)}");
        }
    }

    private static async Task<(object? Result, EvaluationMetrics Metrics)> EvaluateOrAssertAsync(
        EfvibeIntegrationSession session,
        string snippet)
    {
        try
        {
            return await QueryEvaluator.EvaluateAsync(
                session.DbContext,
                session.ScriptSession,
                snippet,
                new DbLogSettings { Enabled = true },
                session.Host.EnumerateLoadedAssemblies());
        }
        catch (EvaluationFailedException failure)
        {
            var detail = failure.Metrics.Warnings.Count > 0
                ? failure.Metrics.Warnings[0]
                : failure.Message;

            Assert.Fail(
                $"efvibe evaluation failed for scenario `{session.Scenario.Id}` ({detail}){Environment.NewLine}{failure}");
            throw;
        }
    }
}