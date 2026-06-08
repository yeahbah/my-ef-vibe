namespace MyEfVibe;

[Flags]
internal enum ProviderCapabilities
{
    None = 0,
    SupportsAutoConstruction = 1,
    SupportsQueryPlan = 2,
    RequiresServerVersion = 4,
    SupportsNamingConventionOverride = 8
}
