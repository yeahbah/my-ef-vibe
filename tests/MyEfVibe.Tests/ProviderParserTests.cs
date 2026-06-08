namespace MyEfVibe.Tests;

public sealed class ProviderParserTests
{
    [Theory]
    [InlineData("npgsql", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("postgres", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("sqlserver", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("sqlite", "Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("oracle", "Oracle.EntityFrameworkCore")]
    [InlineData("mysql", "Pomelo.EntityFrameworkCore.MySql")]
    [InlineData("mariadb", "MariaDB.EntityFrameworkCore")]
    public void TryParseDescriptor_parses_known_aliases(string token, string expectedPackageId)
    {
        Assert.True(ProviderParser.TryParseDescriptor(token, out var descriptor, out var error));
        Assert.Null(error);
        Assert.NotNull(descriptor);
        Assert.Equal(expectedPackageId, descriptor!.PackageId);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("FirebirdSql.EntityFrameworkCore.Firebird")]
    public void TryParseDescriptor_parses_package_ids(string packageId)
    {
        Assert.True(ProviderParser.TryParseDescriptor(packageId, out var descriptor, out var error));
        Assert.Null(error);
        Assert.NotNull(descriptor);
        Assert.Equal(packageId, descriptor!.PackageId);
    }

    [Fact]
    public void TryParseDescriptor_rejects_unknown_token()
    {
        Assert.False(ProviderParser.TryParseDescriptor("not-a-provider", out var descriptor, out var error));

        Assert.Null(descriptor);
        Assert.NotNull(error);
        Assert.Contains("not-a-provider", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOrNull_returns_known_provider_from_package_id()
    {
        var provider = ProviderParser.ParseOrNull("Microsoft.EntityFrameworkCore.Sqlite");

        Assert.NotNull(provider);
        Assert.Equal(MyEfVibeProvider.Sqlite, provider);
    }
}
