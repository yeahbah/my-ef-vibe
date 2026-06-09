namespace MyEfVibe;

internal static class ProviderParser
{
    internal const string ProviderHelpText =
        "Provider alias (sqlserver, npgsql, sqlite, oracle, mysql, mariadb, couchbase) "
        + "or EF package id (for example Microsoft.EntityFrameworkCore.SqlServer "
        + "or FirebirdSql.EntityFrameworkCore.Firebird).";

    internal static bool TryParseDescriptor(
        string? raw,
        out ProviderDescriptor? descriptor,
        out string? errorMessage)
    {
        descriptor = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var token = raw.Trim();

        var knownProvider = ParseKnownProviderAlias(token);

        if (knownProvider.HasValue)
        {
            descriptor = ProviderDescriptor.FromKnownProvider(knownProvider.Value);

            return true;
        }

        if (EntityFrameworkProviderCatalog.TryCreateDescriptorFromProviderToken(token, out descriptor))
        {
            return true;
        }

        errorMessage =
            $"`{token}` is not a recognized EF provider. {ProviderHelpText}";

        return false;
    }

    internal static ProviderDescriptor? ParseDescriptorOrNull(string? raw)
    {
        return TryParseDescriptor(raw, out var descriptor, out _) ? descriptor : null;
    }

    internal static MyEfVibeProvider? ParseOrNull(string? raw)
    {
        return ParseDescriptorOrNull(raw)?.KnownProvider;
    }

    private static MyEfVibeProvider? ParseKnownProviderAlias(string raw)
    {
        if (Enum.TryParse(raw, true, out MyEfVibeProvider direct))
        {
            return direct;
        }

        return raw.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "pg" or "npgsql" => MyEfVibeProvider.Npgsql,

            "mssql" or "sql" or "sqlserver" => MyEfVibeProvider.SqlServer,

            "sqlite" or "sqlitedb" => MyEfVibeProvider.Sqlite,

            "oracle" or "ora" or "odb" => MyEfVibeProvider.Oracle,

            "mysql" or "pomelo" => MyEfVibeProvider.MySql,

            "mariadb" or "maria" => MyEfVibeProvider.MariaDb,

            "couchbase" or "cb" => MyEfVibeProvider.Couchbase,

            _ => null
        };
    }
}
