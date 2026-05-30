namespace MyEfVibe;

internal static class ProviderAssemblyNames
{
    internal static IEnumerable<string> For(MyEfVibeProvider provider) =>
        provider switch
        {
            MyEfVibeProvider.SqlServer =>
            [
                "Microsoft.EntityFrameworkCore.SqlServer",
                "Microsoft.Data.SqlClient",
            ],
            MyEfVibeProvider.Npgsql =>
            [
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                "Npgsql",
            ],
            MyEfVibeProvider.Sqlite =>
            [
                "SQLitePCLRaw.core",
                "SQLitePCLRaw.provider.e_sqlite3",
                "Microsoft.EntityFrameworkCore.Sqlite",
                "Microsoft.Data.Sqlite",
            ],
            MyEfVibeProvider.Oracle =>
            [
                "Oracle.EntityFrameworkCore",
                "Oracle.ManagedDataAccess",
            ],
            MyEfVibeProvider.MySql or MyEfVibeProvider.MariaDb =>
            [
                "Pomelo.EntityFrameworkCore.MySql",
                "MySql.EntityFrameworkCore",
            ],
            _ => [],
        };

    internal static bool IsKnownProviderAssembly(string? assemblySimpleName) =>
        !string.IsNullOrEmpty(assemblySimpleName)
        && KnownProviderAssemblies.Contains(assemblySimpleName);

    internal static string GetNuGetPackageFolderName(string assemblySimpleName) =>
        NuGetPackageFolderAliases.TryGetValue(assemblySimpleName, out var alias)
            ? alias
            : assemblySimpleName.ToLowerInvariant();

    private static readonly HashSet<string> KnownProviderAssemblies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.EntityFrameworkCore.SqlServer",
            "Microsoft.Data.SqlClient",
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            "Npgsql",
            "Microsoft.EntityFrameworkCore.Sqlite",
            "Microsoft.Data.Sqlite",
            "SQLitePCLRaw.core",
            "SQLitePCLRaw.provider.e_sqlite3",
            "SQLitePCLRaw.batteries_v2",
            "Oracle.EntityFrameworkCore",
            "Oracle.ManagedDataAccess",
            "Pomelo.EntityFrameworkCore.MySql",
            "MySql.EntityFrameworkCore",
        };

    /// <summary>
    /// Assembly simple names that differ from the NuGet package id (lowercase folder under ~/.nuget/packages).
    /// </summary>
    private static readonly Dictionary<string, string> NuGetPackageFolderAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Oracle.ManagedDataAccess"] = "oracle.manageddataaccess.core",
            ["Microsoft.Data.SqlClient"] = "microsoft.data.sqlclient",
            ["Microsoft.Data.Sqlite"] = "microsoft.data.sqlite.core",
            ["Microsoft.EntityFrameworkCore.Sqlite"] = "microsoft.entityframeworkcore.sqlite.core",
            ["SQLitePCLRaw.batteries_v2"] = "sqlitepclraw.bundle_e_sqlite3",
        };
}
