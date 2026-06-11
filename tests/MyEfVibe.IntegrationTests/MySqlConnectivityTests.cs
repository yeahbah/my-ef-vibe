namespace MyEfVibe.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public sealed class MySqlConnectivityTests(IntegrationSessionCache sessions)
{
    private readonly IntegrationSessionCache _sessions = sessions;

    [SkippableFact]
    public async Task MySql_database_is_reachable()
    {
        var session = await _sessions.GetAsync("mysql");

        var providerName = DatabaseProbe.ReadProviderName(session.DbContext);
        Assert.Contains("MySql", providerName, StringComparison.OrdinalIgnoreCase);
        Assert.True(DatabaseProbe.ProviderMatches(session.Scenario, providerName));
    }

    [SkippableFact]
    public async Task MySql_products_query_materializes()
    {
        var session = await _sessions.GetAsync("mysql");

        var (_, metrics) = await QueryEvaluator.EvaluateAsync(
            session.DbContext,
            session.ScriptSession,
            "db.Products.Take(3).ToList();",
            new DbLogSettings { Enabled = true },
            session.Host.EnumerateLoadedAssemblies());

        Assert.True(metrics.Succeeded);
        Assert.True(metrics.RowCount >= 1);

        var sql = metrics.ExecutedSql.Count > 0
            ? string.Join(Environment.NewLine, metrics.ExecutedSql)
            : metrics.TranslatedSql;

        Assert.False(string.IsNullOrWhiteSpace(sql));
        Assert.Contains("product", sql, StringComparison.OrdinalIgnoreCase);
    }
}
