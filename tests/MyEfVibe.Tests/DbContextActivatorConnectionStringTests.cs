namespace MyEfVibe.Tests;

public sealed class DbContextActivatorConnectionStringTests
{
    [Fact]
    public void NormalizeConnectionStringForProvider_leaves_mysql_connection_string_unchanged()
    {
        const string connectionString =
            "Server=localhost;Port=3306;Database=AdventureWorks;User=root;Password=secret;";

        var normalized = DbContextActivator.NormalizeConnectionStringForProvider(
            connectionString,
            ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.MySql));

        Assert.Equal(connectionString, normalized);
    }

    [Fact]
    public void NormalizeConnectionStringForProvider_normalizes_sqlserver_connection_string()
    {
        const string connectionString =
            "Server=localhost,1433;Database=AdventureWorks;User Id=sa;Password=secret";

        var normalized = DbContextActivator.NormalizeConnectionStringForProvider(
            connectionString,
            ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.SqlServer));

        Assert.Contains("Encrypt=False", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TrustServerCertificate=True", normalized, StringComparison.OrdinalIgnoreCase);
    }
}
