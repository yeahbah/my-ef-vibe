namespace MyEfVibe.Tests;

public sealed class ProviderCapabilityResolverTests
{
    [Fact]
    public void ResolveFeatureTier_unknown_provider_is_sql_tier_without_query_plan()
    {
        var descriptor = EntityFrameworkProviderCatalog.CreateDescriptor(
            "FirebirdSql.EntityFrameworkCore.Firebird");

        var tier = ProviderCapabilityResolver.ResolveFeatureTier(descriptor);

        Assert.Equal(FeatureTier.Sql, tier);
        Assert.False(ProviderCapabilityResolver.SupportsQueryPlan(descriptor, new object()));
    }

    [Fact]
    public void ResolveFeatureTier_sqlserver_has_query_plan_tier()
    {
        var descriptor = ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.SqlServer);

        Assert.Equal(FeatureTier.QueryPlan, ProviderCapabilityResolver.ResolveFeatureTier(descriptor));
        Assert.True(ProviderCapabilityResolver.SupportsQueryPlan(descriptor, new object()));
    }

    [Fact]
    public void ResolveFeatureTier_postgresql_has_conventions_tier()
    {
        var descriptor = ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Npgsql);

        Assert.Equal(FeatureTier.Conventions, ProviderCapabilityResolver.ResolveFeatureTier(descriptor));
    }

    [Fact]
    public void DescribeUnavailableQueryPlan_mentions_provider_package()
    {
        var descriptor = EntityFrameworkProviderCatalog.CreateDescriptor(
            "FirebirdSql.EntityFrameworkCore.Firebird");

        var message = ProviderCapabilityResolver.DescribeUnavailableQueryPlan(descriptor);

        Assert.Contains("FirebirdSql.EntityFrameworkCore.Firebird", message, StringComparison.Ordinal);
        Assert.Contains("LINQ", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportsNamingConventionOverride_only_for_postgresql_and_sqlite()
    {
        Assert.True(
            ProviderCapabilityResolver.SupportsNamingConventionOverride(
                ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Npgsql)));
        Assert.True(
            ProviderCapabilityResolver.SupportsNamingConventionOverride(
                ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Sqlite)));
        Assert.False(
            ProviderCapabilityResolver.SupportsNamingConventionOverride(
                ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.SqlServer)));
    }
}
