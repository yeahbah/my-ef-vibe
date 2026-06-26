using System.Data.Common;

namespace MyEfVibe;

internal static class LiveDatabaseTableCatalog
{
    internal sealed record TableRef(string? Schema, string TableName);

    internal static IReadOnlyList<TableRef> TryListTables(DbConnection connection)
    {
        try
        {
            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            var provider = connection.GetType().FullName ?? string.Empty;

            if (provider.Contains("Npgsql", StringComparison.Ordinal))
            {
                return ListFromQuery(
                    connection,
                    """
                    SELECT table_schema, table_name
                    FROM information_schema.tables
                    WHERE table_type = 'BASE TABLE'
                      AND table_schema NOT IN ('pg_catalog', 'information_schema')
                    """);
            }

            if (provider.Contains("Sqlite", StringComparison.Ordinal))
            {
                return ListFromQuery(
                    connection,
                    """
                    SELECT NULL AS table_schema, name AS table_name
                    FROM sqlite_master
                    WHERE type = 'table'
                      AND name NOT LIKE 'sqlite_%'
                    """);
            }

            if (provider.Contains("SqlConnection", StringComparison.Ordinal)
                || provider.Contains("Microsoft.Data.SqlClient", StringComparison.Ordinal))
            {
                return ListFromQuery(
                    connection,
                    """
                    SELECT table_schema, table_name
                    FROM INFORMATION_SCHEMA.TABLES
                    WHERE table_type = 'BASE TABLE'
                    """);
            }

            if (provider.Contains("Oracle", StringComparison.Ordinal))
            {
                return ListFromQuery(
                    connection,
                    """
                    SELECT owner AS table_schema, table_name
                    FROM all_tables
                    WHERE owner NOT IN ('SYS', 'SYSTEM')
                    """);
            }

            return ListFromQuery(
                connection,
                """
                SELECT table_schema, table_name
                FROM information_schema.tables
                WHERE table_type = 'BASE TABLE'
                """);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<TableRef> ListFromQuery(DbConnection connection, string sql)
    {
        var tables = new List<TableRef>();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (reader.IsDBNull(1))
            {
                continue;
            }

            var schema = reader.IsDBNull(0) ? null : reader.GetString(0);
            var table = reader.GetString(1);

            if (string.IsNullOrWhiteSpace(table))
            {
                continue;
            }

            tables.Add(new TableRef(schema, table));
        }

        return tables;
    }
}
