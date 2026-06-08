namespace MyEfVibe.Tests;

public sealed class PomeloMySqlProviderConfiguratorTests
{
    private readonly PomeloMySqlProviderConfigurator _configurator = new();

    [Fact]
    public void CanHandle_accepts_pomelo_mysql_descriptor()
    {
        Assert.True(_configurator.CanHandle(ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.MySql)));
    }

    [Fact]
    public void CanHandle_rejects_mariadb_oracle_package()
    {
        Assert.False(_configurator.CanHandle(ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.MariaDb)));
    }

    [Fact]
    public void CanHandle_rejects_oracle_mysql_package()
    {
        var oracleMySql = EntityFrameworkProviderCatalog.CreateDescriptor("MySql.EntityFrameworkCore");

        Assert.False(_configurator.CanHandle(oracleMySql));
    }

    [Fact]
    public void CanHandle_rejects_non_mysql_providers()
    {
        Assert.False(_configurator.CanHandle(ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Npgsql)));
    }
}
