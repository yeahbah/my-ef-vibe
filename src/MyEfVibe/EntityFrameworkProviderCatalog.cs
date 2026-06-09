namespace MyEfVibe;

internal static class EntityFrameworkProviderCatalog
{
    private static readonly string[] ExcludedPackageSuffixes =
    [
        ".Design",
        ".Tools",
        ".Analyzers",
        ".Proxies",
        ".Abstractions",
        ".InMemory"
    ];

    private static readonly HashSet<string> ExcludedPackageIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Relational"
        };

    private static readonly ProviderSeed[] KnownSeeds =
    [
        new(
            "Microsoft.EntityFrameworkCore.SqlServer",
            "Microsoft.EntityFrameworkCore.SqlServer",
            "UseSqlServer",
            MyEfVibeProvider.SqlServer,
            ProviderCapabilities.SupportsAutoConstruction | ProviderCapabilities.SupportsQueryPlan),
        new(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            "UseNpgsql",
            MyEfVibeProvider.Npgsql,
            ProviderCapabilities.SupportsAutoConstruction
                | ProviderCapabilities.SupportsQueryPlan
                | ProviderCapabilities.SupportsNamingConventionOverride),
        new(
            "Microsoft.EntityFrameworkCore.Sqlite",
            "Microsoft.EntityFrameworkCore.Sqlite",
            "UseSqlite",
            MyEfVibeProvider.Sqlite,
            ProviderCapabilities.SupportsAutoConstruction
                | ProviderCapabilities.SupportsQueryPlan
                | ProviderCapabilities.SupportsNamingConventionOverride),
        new(
            "Oracle.EntityFrameworkCore",
            "Oracle.EntityFrameworkCore",
            "UseOracle",
            MyEfVibeProvider.Oracle,
            ProviderCapabilities.SupportsAutoConstruction | ProviderCapabilities.SupportsQueryPlan),
        new(
            "MariaDB.EntityFrameworkCore",
            "MariaDB.EntityFrameworkCore",
            "UseMySQL",
            MyEfVibeProvider.MariaDb,
            ProviderCapabilities.SupportsAutoConstruction
                | ProviderCapabilities.SupportsQueryPlan
                | ProviderCapabilities.RequiresServerVersion),
        new(
            "Pomelo.EntityFrameworkCore.MySql",
            "Pomelo.EntityFrameworkCore.MySql",
            "UseMySql",
            MyEfVibeProvider.MySql,
            ProviderCapabilities.SupportsAutoConstruction
                | ProviderCapabilities.SupportsQueryPlan
                | ProviderCapabilities.RequiresServerVersion),
        new(
            "MySql.EntityFrameworkCore",
            "MySql.EntityFrameworkCore",
            "UseMySQL",
            MyEfVibeProvider.MySql,
            ProviderCapabilities.SupportsAutoConstruction | ProviderCapabilities.SupportsQueryPlan),
        new(
            "Couchbase.EntityFrameworkCore",
            "Couchbase.EntityFrameworkCore",
            "UseCouchbase",
            MyEfVibeProvider.Couchbase,
            ProviderCapabilities.SupportsAutoConstruction | ProviderCapabilities.RequiresAsyncQueries)
    ];

    internal static bool IsEntityFrameworkProviderPackage(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId) || ExcludedPackageIds.Contains(packageId))
        {
            return false;
        }

        foreach (var suffix in ExcludedPackageSuffixes)
        {
            if (packageId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (TryGetKnownSeed(packageId, out _))
        {
            return true;
        }

        return packageId.Contains(".EntityFrameworkCore.", StringComparison.OrdinalIgnoreCase)
               || packageId.EndsWith(".EntityFrameworkCore", StringComparison.OrdinalIgnoreCase);
    }

    internal static ProviderDescriptor CreateDescriptor(string packageId)
    {
        if (TryGetKnownSeed(packageId, out var seed))
        {
            return seed.ToDescriptor();
        }

        return new ProviderDescriptor(
            packageId,
            packageId,
            ExtensionMethodName: null,
            KnownProvider: null,
            ProviderCapabilities.SupportsAutoConstruction);
    }

    internal static ProviderDescriptor? CreateDescriptorForKnownProvider(MyEfVibeProvider provider)
    {
        foreach (var seed in KnownSeeds)
        {
            if (seed.KnownProvider == provider)
            {
                return seed.ToDescriptor();
            }
        }

        return null;
    }

    private static bool TryGetKnownSeed(string packageId, out ProviderSeed seed)
    {
        foreach (var candidate in KnownSeeds)
        {
            if (string.Equals(candidate.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                seed = candidate;

                return true;
            }
        }

        seed = default!;

        return false;
    }

    internal static bool TryCreateDescriptorFromProviderToken(string token, out ProviderDescriptor? descriptor)
    {
        descriptor = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();

        if (IsEntityFrameworkProviderPackage(trimmed))
        {
            descriptor = CreateDescriptor(trimmed);

            return true;
        }

        foreach (var seed in KnownSeeds)
        {
            if (string.Equals(trimmed, seed.PackageId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, seed.ProviderAssemblyName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, seed.ExtensionMethodName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    trimmed,
                    seed.PackageId.Split('.').Last(),
                    StringComparison.OrdinalIgnoreCase))
            {
                descriptor = seed.ToDescriptor();

                return true;
            }
        }

        return false;
    }

    internal static string GetNuGetPackageFolderName(string packageId)
    {
        return packageId.ToLowerInvariant();
    }

    private sealed record ProviderSeed(
        string PackageId,
        string ProviderAssemblyName,
        string ExtensionMethodName,
        MyEfVibeProvider KnownProvider,
        ProviderCapabilities Capabilities)
    {
        internal ProviderDescriptor ToDescriptor()
        {
            return new ProviderDescriptor(
                PackageId,
                ProviderAssemblyName,
                ExtensionMethodName,
                KnownProvider,
                Capabilities);
        }
    }
}
