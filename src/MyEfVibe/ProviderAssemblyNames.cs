namespace MyEfVibe;

internal static class ProviderAssemblyNames
{
    /// <summary>
    ///     Assembly simple names that differ from the NuGet package id (lowercase folder under ~/.nuget/packages).
    /// </summary>
    private static readonly Dictionary<string, string> NuGetPackageFolderAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Oracle.ManagedDataAccess"] = "oracle.manageddataaccess.core",
            ["Microsoft.Data.SqlClient"] = "microsoft.data.sqlclient",
            ["Microsoft.Data.Sqlite"] = "microsoft.data.sqlite.core",
            ["Microsoft.EntityFrameworkCore.Sqlite"] = "microsoft.entityframeworkcore.sqlite.core",
            ["SQLitePCLRaw.batteries_v2"] = "sqlitepclraw.bundle_e_sqlite3"
        };

    internal static IEnumerable<string> For(ProviderDescriptor descriptor)
    {
        yield return descriptor.ProviderAssemblyName;

        if (descriptor.KnownProvider is not { } knownProvider)
        {
            yield break;
        }

        foreach (var assemblySimpleName in For(knownProvider))
        {
            if (!string.Equals(
                    assemblySimpleName,
                    descriptor.ProviderAssemblyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                yield return assemblySimpleName;
            }
        }
    }

    internal static IEnumerable<string> For(MyEfVibeProvider provider)
    {
        return provider switch
        {
            MyEfVibeProvider.SqlServer =>
            [
                "Microsoft.EntityFrameworkCore.SqlServer",
                "Microsoft.Data.SqlClient"
            ],
            MyEfVibeProvider.Npgsql =>
            [
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                "Npgsql"
            ],
            MyEfVibeProvider.Sqlite =>
            [
                "SQLitePCLRaw.core",
                "SQLitePCLRaw.provider.e_sqlite3",
                "SQLitePCLRaw.batteries_v2",
                "Microsoft.EntityFrameworkCore.Sqlite",
                "Microsoft.Data.Sqlite"
            ],
            MyEfVibeProvider.Oracle =>
            [
                "Oracle.EntityFrameworkCore",
                "Oracle.ManagedDataAccess"
            ],
            MyEfVibeProvider.MySql or MyEfVibeProvider.MariaDb =>
            [
                "Pomelo.EntityFrameworkCore.MySql",
                "MySql.EntityFrameworkCore"
            ],
            _ => []
        };
    }

    internal static string GetNuGetPackageFolderName(string assemblySimpleName)
    {
        return NuGetPackageFolderAliases.TryGetValue(assemblySimpleName, out var alias)
            ? alias
            : assemblySimpleName.ToLowerInvariant();
    }
}