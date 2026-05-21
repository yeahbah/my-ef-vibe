namespace MyEfVibe.IntegrationTests;

[CollectionDefinition(IntegrationCollection.Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationSessionCache>
{
    internal const string Name = "Integration";
}

public sealed class IntegrationSessionCache : IAsyncLifetime
{
    private readonly Dictionary<string, EfvibeIntegrationSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    internal async Task<EfvibeIntegrationSession> GetAsync(
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(scenarioId, out var existing))
            return existing;

        var session = await IntegrationTestGuards.RequireSessionAsync(scenarioId, cancellationToken);
        _sessions[scenarioId] = session;
        return session;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var session in _sessions.Values)
            await session.DisposeAsync();

        _sessions.Clear();
    }
}
