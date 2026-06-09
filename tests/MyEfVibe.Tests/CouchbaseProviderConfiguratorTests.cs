namespace MyEfVibe.Tests;

public sealed class CouchbaseProviderConfiguratorTests
{
    private readonly CouchbaseSettingsProviderConfigurator _settingsConfigurator = new();
    private readonly CouchbaseEntityFrameworkProviderConfigurator _relationalConfigurator = new();

    [Fact]
    public void CanHandle_accepts_couchbase_descriptor()
    {
        var descriptor = ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Couchbase);

        Assert.True(_settingsConfigurator.CanHandle(descriptor));
        Assert.True(_relationalConfigurator.CanHandle(descriptor));
    }

    [Fact]
    public void CanHandle_rejects_sqlserver_descriptor()
    {
        var descriptor = ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.SqlServer);

        Assert.False(_settingsConfigurator.CanHandle(descriptor));
        Assert.False(_relationalConfigurator.CanHandle(descriptor));
    }
}
