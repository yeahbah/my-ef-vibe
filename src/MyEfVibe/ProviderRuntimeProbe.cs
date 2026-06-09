namespace MyEfVibe;

internal static class ProviderRuntimeProbe
{
    internal static MyEfVibeProvider? TryResolveKnownProvider(object dbContext)
    {
        var database = dbContext.GetType().GetProperty("Database")?.GetValue(dbContext);

        if (database is null)
        {
            return null;
        }

        var providerName = database.GetType().GetProperty("ProviderName")?.GetValue(database) as string;

        return TryMapProviderName(providerName);
    }

    internal static MyEfVibeProvider? TryMapProviderName(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return null;
        }

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return MyEfVibeProvider.Npgsql;
        }

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return MyEfVibeProvider.Sqlite;
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return MyEfVibeProvider.SqlServer;
        }

        if (providerName.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
        {
            return MyEfVibeProvider.Oracle;
        }

        if (providerName.Contains("MariaDb", StringComparison.OrdinalIgnoreCase)
            || providerName.Contains("MariaDB", StringComparison.Ordinal))
        {
            return MyEfVibeProvider.MariaDb;
        }

        if (providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase)
            || providerName.Contains("MySQL", StringComparison.Ordinal))
        {
            return MyEfVibeProvider.MySql;
        }

        if (providerName.Contains("Couchbase", StringComparison.OrdinalIgnoreCase))
        {
            return MyEfVibeProvider.Couchbase;
        }

        if (providerName.Contains("Firebird", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return null;
    }
}
