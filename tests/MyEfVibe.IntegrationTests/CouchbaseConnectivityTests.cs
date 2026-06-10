namespace MyEfVibe.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public sealed class CouchbaseConnectivityTests(IntegrationSessionCache sessions)
{
    private readonly IntegrationSessionCache _sessions = sessions;

    [SkippableFact]
    public async Task Couchbase_database_is_reachable()
    {
        var session = await _sessions.GetAsync("couchbase");

        var providerName = DatabaseProbe.ReadProviderName(session.DbContext);
        Assert.Contains("Couchbase", providerName, StringComparison.OrdinalIgnoreCase);
        Assert.True(DatabaseProbe.ProviderMatches(session.Scenario, providerName));
    }

    [SkippableFact]
    public async Task Couchbase_products_count_materializes()
    {
        var session = await _sessions.GetAsync("couchbase");

        var (result, metrics) = await QueryEvaluator.EvaluateAsync(
            session.DbContext,
            session.ScriptSession,
            "db.Products.Count();",
            new DbLogSettings { Enabled = true },
            session.Host.EnumerateLoadedAssemblies());

        Assert.True(metrics.Succeeded);
        Assert.IsType<int>(result);
        Assert.True((int)result > 0);
    }

    [SkippableFact]
    public async Task Couchbase_products_projection_materializes()
    {
        var session = await _sessions.GetAsync("couchbase");

        var (result, metrics) = await QueryEvaluator.EvaluateAsync(
            session.DbContext,
            session.ScriptSession,
            "db.Products.AsNoTracking().Select(x => new { x.ProductId, x.Name }).Take(3).ToArray();",
            new DbLogSettings { Enabled = true },
            session.Host.EnumerateLoadedAssemblies());

        Assert.True(metrics.Succeeded);
        Assert.True(metrics.RowCount >= 1);

        var rows = Assert.IsAssignableFrom<System.Collections.IEnumerable>(result);
        var first = rows.Cast<object>().First();

        var productId = first.GetType().GetProperty("ProductId")?.GetValue(first);
        var name = first.GetType().GetProperty("Name")?.GetValue(first) as string;

        Assert.NotNull(productId);
        Assert.True((int)productId > 0);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }
}
