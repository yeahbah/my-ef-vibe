namespace MyEfVibe;

internal static class ProviderCapabilityResolver
{
    internal static ProviderCapabilities ResolveEffectiveCapabilities(
        ProviderDescriptor? configuredProvider,
        object? dbContext = null)
    {
        var capabilities = configuredProvider?.Capabilities ?? ProviderCapabilities.SupportsAutoConstruction;

        if (dbContext is null)
        {
            return capabilities;
        }

        var runtimeProvider = ProviderRuntimeProbe.TryResolveKnownProvider(dbContext);

        if (runtimeProvider is null)
        {
            return capabilities;
        }

        var runtimeDescriptor = ProviderDescriptor.TryFromKnownProvider(runtimeProvider);

        if (runtimeDescriptor is null)
        {
            return capabilities;
        }

        return capabilities | runtimeDescriptor.Capabilities;
    }

    internal static bool SupportsQueryPlan(ProviderDescriptor? configuredProvider, object dbContext)
    {
        return ResolveEffectiveCapabilities(configuredProvider, dbContext)
            .HasFlag(ProviderCapabilities.SupportsQueryPlan);
    }

    internal static bool SupportsNamingConventionOverride(ProviderDescriptor? configuredProvider)
    {
        return configuredProvider?.SupportsNamingConventionOverride == true;
    }

    internal static FeatureTier ResolveFeatureTier(
        ProviderDescriptor? configuredProvider,
        object? dbContext = null)
    {
        return ResolveFeatureTier(ResolveEffectiveCapabilities(configuredProvider, dbContext));
    }

    internal static FeatureTier ResolveFeatureTier(ProviderCapabilities capabilities)
    {
        if (capabilities.HasFlag(ProviderCapabilities.SupportsNamingConventionOverride))
        {
            return FeatureTier.Conventions;
        }

        if (capabilities.HasFlag(ProviderCapabilities.SupportsQueryPlan))
        {
            return FeatureTier.QueryPlan;
        }

        if (capabilities.HasFlag(ProviderCapabilities.SupportsAutoConstruction))
        {
            return FeatureTier.Sql;
        }

        return FeatureTier.Construct;
    }

    internal static string DescribeUnavailableQueryPlan(ProviderDescriptor? configuredProvider)
    {
        var label = string.IsNullOrWhiteSpace(configuredProvider?.PackageId)
            ? "this provider"
            : configuredProvider.PackageId;

        return $":plan is not available for {label} yet. LINQ queries and SQL translation still work.";
    }
}
