namespace MyEfVibe;

internal static class ProviderParser
{
    internal static MyEfVibeProvider? ParseOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (Enum.TryParse(raw, ignoreCase: true, out MyEfVibeProvider direct))
            return direct;

        return raw.Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "pg" or "npgsql" => MyEfVibeProvider.Npgsql,

            "mssql" or "sql" or "sqlserver" => MyEfVibeProvider.SqlServer,

            "sqlite" or "sqlitedb" => MyEfVibeProvider.Sqlite,

            "oracle" or "ora" or "odb" => MyEfVibeProvider.Oracle,

            "mysql" or "mariadb" or "pomelo" => MyEfVibeProvider.MySql,

            _ => null,

        };
    }
}
