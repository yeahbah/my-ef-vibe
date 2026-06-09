namespace MyEfVibe;

internal sealed record ProviderDescriptor(
    string PackageId,
    string ProviderAssemblyName,
    string? ExtensionMethodName,
    MyEfVibeProvider? KnownProvider = null,
    ProviderCapabilities Capabilities = ProviderCapabilities.SupportsAutoConstruction)
{
    internal bool IsSqlite =>
        KnownProvider == MyEfVibeProvider.Sqlite
        || PackageId.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

    internal bool IsSqlServer =>
        KnownProvider == MyEfVibeProvider.SqlServer
        || PackageId.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);

    internal bool SupportsQueryPlan =>
        Capabilities.HasFlag(ProviderCapabilities.SupportsQueryPlan);

    internal bool SupportsNamingConventionOverride =>
        Capabilities.HasFlag(ProviderCapabilities.SupportsNamingConventionOverride);

    internal bool RequiresAsyncQueries =>
        Capabilities.HasFlag(ProviderCapabilities.RequiresAsyncQueries)
        || KnownProvider == MyEfVibeProvider.Couchbase
        || PackageId.Contains("Couchbase.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase);

    internal bool IsCouchbase =>
        KnownProvider == MyEfVibeProvider.Couchbase
        || PackageId.Contains("Couchbase.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase);

    internal FeatureTier ResolveFeatureTier(object? dbContext = null)
    {
        return ProviderCapabilityResolver.ResolveFeatureTier(this, dbContext);
    }

    internal static ProviderDescriptor FromKnownProvider(MyEfVibeProvider provider)
    {
        return EntityFrameworkProviderCatalog.CreateDescriptorForKnownProvider(provider)
               ?? throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported provider.");
    }

    internal static ProviderDescriptor? TryFromKnownProvider(MyEfVibeProvider? provider)
    {
        return provider.HasValue
            ? EntityFrameworkProviderCatalog.CreateDescriptorForKnownProvider(provider.Value)
            : null;
    }
}
