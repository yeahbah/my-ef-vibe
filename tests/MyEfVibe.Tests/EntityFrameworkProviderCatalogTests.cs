namespace MyEfVibe.Tests;

public sealed class EntityFrameworkProviderCatalogTests
{
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Design")]
    [InlineData("Microsoft.EntityFrameworkCore.Tools")]
    [InlineData("Microsoft.EntityFrameworkCore.InMemory")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    public void IsEntityFrameworkProviderPackage_excludes_non_provider_packages(string packageId)
    {
        Assert.False(EntityFrameworkProviderCatalog.IsEntityFrameworkProviderPackage(packageId));
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("FirebirdSql.EntityFrameworkCore.Firebird")]
    [InlineData("Microsoft.EntityFrameworkCore.Cosmos")]
    public void IsEntityFrameworkProviderPackage_accepts_relational_and_third_party_packages(string packageId)
    {
        Assert.True(EntityFrameworkProviderCatalog.IsEntityFrameworkProviderPackage(packageId));
    }

    [Fact]
    public void CreateDescriptor_unknown_provider_has_auto_construction_only()
    {
        var descriptor = EntityFrameworkProviderCatalog.CreateDescriptor(
            "FirebirdSql.EntityFrameworkCore.Firebird");

        Assert.Equal("FirebirdSql.EntityFrameworkCore.Firebird", descriptor.PackageId);
        Assert.Null(descriptor.KnownProvider);
        Assert.True(descriptor.Capabilities.HasFlag(ProviderCapabilities.SupportsAutoConstruction));
        Assert.False(descriptor.Capabilities.HasFlag(ProviderCapabilities.SupportsQueryPlan));
    }

    [Fact]
    public void CreateDescriptor_pomelo_marks_server_version_requirement()
    {
        var descriptor = EntityFrameworkProviderCatalog.CreateDescriptor("Pomelo.EntityFrameworkCore.MySql");

        Assert.True(descriptor.Capabilities.HasFlag(ProviderCapabilities.RequiresServerVersion));
        Assert.True(descriptor.Capabilities.HasFlag(ProviderCapabilities.SupportsQueryPlan));
    }

    [Fact]
    public void CreateDescriptor_couchbase_requires_async_queries()
    {
        var descriptor = EntityFrameworkProviderCatalog.CreateDescriptor("Couchbase.EntityFrameworkCore");

        Assert.Equal(MyEfVibeProvider.Couchbase, descriptor.KnownProvider);
        Assert.True(descriptor.Capabilities.HasFlag(ProviderCapabilities.RequiresAsyncQueries));
        Assert.False(descriptor.Capabilities.HasFlag(ProviderCapabilities.SupportsQueryPlan));
    }

    [Theory]
    [InlineData("Couchbase.EntityFrameworkCore", "Couchbase.EntityFrameworkCore")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("FirebirdSql.EntityFrameworkCore.Firebird", "FirebirdSql.EntityFrameworkCore.Firebird")]
    public void TryCreateDescriptorFromProviderToken_parses_aliases_and_package_ids(
        string token,
        string expectedPackageId)
    {
        Assert.True(
            EntityFrameworkProviderCatalog.TryCreateDescriptorFromProviderToken(token, out var descriptor));

        Assert.NotNull(descriptor);
        Assert.Equal(expectedPackageId, descriptor!.PackageId);
    }
}
