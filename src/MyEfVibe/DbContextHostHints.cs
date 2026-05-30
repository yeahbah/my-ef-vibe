using System.Reflection;

namespace MyEfVibe;

/// <summary>
///     Applies host-specific hints to DbContext instances built without DI (for example AdventureWorks
///     <c>_databaseProvider</c> must be PostgreSQL before the first query so <c>OnModelCreating</c> applies
///     lowercase schema/table names).
/// </summary>
internal static class DbContextHostHints
{
    internal static void TryApplyPostgreSqlNamingHint(
        object dbContextInstance,
        string startupProjectPath,
        MyEfVibeProvider providerKey)
    {
        var settingsProvider = AppSettingsConnectionResolver.TryReadDatabaseProviderName(startupProjectPath);
        var usePostgreSql = providerKey == MyEfVibeProvider.Npgsql
                            || LooksLikePostgreSqlProviderName(settingsProvider);

        if (!usePostgreSql)
        {
            return;
        }

        TrySetDatabaseProviderField(dbContextInstance, "PostgreSQL");
    }

    private static bool LooksLikePostgreSqlProviderName(string? providerName)
    {
        return !string.IsNullOrWhiteSpace(providerName)
               && providerName.Contains("postgres", StringComparison.OrdinalIgnoreCase);
    }

    private static void TrySetDatabaseProviderField(object dbContextInstance, string value)
    {
        for (var type = dbContextInstance.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(
                "_databaseProvider",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field?.FieldType == typeof(string))
            {
                field.SetValue(dbContextInstance, value);
                return;
            }
        }
    }
}