using System.Data.Common;

namespace MyEfVibe;

/// <summary>
///     Detects PostgreSQL identifier conventions: pgloader-style lowercase
///     (<c>production.product</c>) or AdventureWorks PascalCase with <c>*ID</c> columns
///     (<c>"Production"."Product"</c> / <c>ProductID</c>).
/// </summary>
internal static class PostgreSqlNamingProbe
{
    private const string ProbeSchema = "production";
    private const string ProbeTable = "product";
    private const string PascalCaseProbeSchema = "Production";
    private const string PascalCaseProbeTable = "Product";
    private const string AdventureWorksIdColumn = "ProductID";

    internal static bool RequiresLowercaseMapping(WorkspaceHost host, string connectionString)
    {
        return Detect(host, connectionString) == PostgreSqlNamingStyle.LowercasePgloader;
    }

    internal static PostgreSqlNamingStyle Detect(WorkspaceHost host, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return PostgreSqlNamingStyle.None;
        }

        try
        {
            using var connection = TryOpenConnection(host, connectionString);

            if (connection is null)
            {
                return PostgreSqlNamingStyle.None;
            }

            var hasLowercase = TableExists(connection, ProbeSchema, ProbeTable);
            var hasPascalCase = TableExists(connection, PascalCaseProbeSchema, PascalCaseProbeTable);

            if (hasLowercase && !hasPascalCase)
            {
                return PostgreSqlNamingStyle.LowercasePgloader;
            }

            if (hasPascalCase
                && ColumnExists(connection, PascalCaseProbeSchema, PascalCaseProbeTable, AdventureWorksIdColumn))
            {
                return PostgreSqlNamingStyle.AdventureWorksPascalCase;
            }

            return PostgreSqlNamingStyle.None;
        }
        catch
        {
            return PostgreSqlNamingStyle.None;
        }
    }

    internal static Dictionary<(string Schema, string Table), HashSet<string>> LoadColumnNameIndex(
        WorkspaceHost host,
        string connectionString)
    {
        var index = new Dictionary<(string Schema, string Table), HashSet<string>>(
            StringTupleComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return index;
        }

        try
        {
            using var connection = TryOpenConnection(host, connectionString);

            if (connection is null)
            {
                return index;
            }

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT table_schema, table_name, column_name
                FROM information_schema.columns
                WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
                """;

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
                {
                    continue;
                }

                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                var column = reader.GetString(2);
                var key = (schema, table);

                if (!index.TryGetValue(key, out var columns))
                {
                    columns = new HashSet<string>(StringComparer.Ordinal);
                    index[key] = columns;
                }

                columns.Add(column);
            }
        }
        catch
        {
            index.Clear();
        }

        return index;
    }

    private static DbConnection? TryOpenConnection(WorkspaceHost host, string connectionString)
    {
        host.PreloadPackageByName("Npgsql");

        var npgsqlAssembly = host.LoadAssembly("Npgsql");

        if (npgsqlAssembly is null)
        {
            return null;
        }

        var connectionType = npgsqlAssembly.GetType("Npgsql.NpgsqlConnection", false);

        if (connectionType is null
            || Activator.CreateInstance(connectionType, connectionString) is not DbConnection connection)
        {
            return null;
        }

        connection.Open();

        return connection;
    }

    private static bool TableExists(DbConnection connection, string schema, string table)
    {
        return RelationExists(
            connection,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema
                  AND table_name = @table
            )
            """,
            schema,
            table);
    }

    private static bool ColumnExists(DbConnection connection, string schema, string table, string column)
    {
        return RelationExists(
            connection,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = @schema
                  AND table_name = @table
                  AND column_name = @column
            )
            """,
            schema,
            table,
            column);
    }

    private static bool RelationExists(
        DbConnection connection,
        string sql,
        string schema,
        string table,
        string? column = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var schemaParameter = command.CreateParameter();
        schemaParameter.ParameterName = "schema";
        schemaParameter.Value = schema;
        command.Parameters.Add(schemaParameter);

        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "table";
        tableParameter.Value = table;
        command.Parameters.Add(tableParameter);

        if (column is not null)
        {
            var columnParameter = command.CreateParameter();
            columnParameter.ParameterName = "column";
            columnParameter.Value = column;
            command.Parameters.Add(columnParameter);
        }

        return command.ExecuteScalar() is true or 1;
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Schema, string Table)>
    {
        internal static readonly StringTupleComparer OrdinalIgnoreCase = new();

        public bool Equals((string Schema, string Table) x, (string Schema, string Table) y)
        {
            return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Schema, string Table) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table));
        }
    }
}
