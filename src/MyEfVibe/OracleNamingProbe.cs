using System.Data.Common;

namespace MyEfVibe;

/// <summary>
///     Detects Oracle identifier conventions: native Data Pump dumps use uppercase unquoted
///     identifiers (<c>PRODUCTION.PRODUCT</c>) while some converted databases keep quoted PascalCase
///     (<c>"Production"."Product"</c>) that already match EF fluent mappings.
/// </summary>
internal static class OracleNamingProbe
{
    private const string UppercaseProbeOwner = "PRODUCTION";
    private const string UppercaseProbeTable = "PRODUCT";
    private const string PascalCaseProbeOwner = "Production";
    private const string PascalCaseProbeTable = "Product";

    internal static Dictionary<(string Schema, string Table), Dictionary<string, string>> LoadColumnMetadataIndex(
        WorkspaceHost host,
        string connectionString)
    {
        var index = new Dictionary<(string Schema, string Table), Dictionary<string, string>>(
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
                SELECT owner, table_name, column_name, data_type
                FROM all_tab_columns
                WHERE owner IN ('PRODUCTION', 'PERSON', 'SALES', 'HUMANRESOURCES', 'PURCHASING')
                """;

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3))
                {
                    continue;
                }

                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                var column = reader.GetString(2);
                var dataType = reader.GetString(3);
                var key = (schema, table);

                if (!index.TryGetValue(key, out var columns))
                {
                    columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    index[key] = columns;
                }

                columns[column] = dataType;
            }
        }
        catch
        {
            index.Clear();
        }

        return index;
    }

    internal static OracleNamingStyle Detect(WorkspaceHost host, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return OracleNamingStyle.None;
        }

        try
        {
            using var connection = TryOpenConnection(host, connectionString);

            if (connection is null)
            {
                return OracleNamingStyle.None;
            }

            var hasUppercase = TableExists(connection, UppercaseProbeOwner, UppercaseProbeTable);
            var hasPascalCase = TableExists(connection, PascalCaseProbeOwner, PascalCaseProbeTable);

            if (hasUppercase && !hasPascalCase)
            {
                return OracleNamingStyle.NativeUppercase;
            }

            return OracleNamingStyle.None;
        }
        catch
        {
            return OracleNamingStyle.None;
        }
    }

    private static DbConnection? TryOpenConnection(WorkspaceHost host, string connectionString)
    {
        foreach (var assemblySimpleName in new[] { "Oracle.ManagedDataAccess", "Oracle.ManagedDataAccess.Core" })
        {
            host.PreloadPackageByName(assemblySimpleName);

            var oracleAssembly = host.LoadAssembly(assemblySimpleName);

            if (oracleAssembly is null)
            {
                continue;
            }

            var connectionType = oracleAssembly.GetType("Oracle.ManagedDataAccess.Client.OracleConnection", false);

            if (connectionType is null
                || Activator.CreateInstance(connectionType, connectionString) is not DbConnection connection)
            {
                continue;
            }

            connection.Open();

            return connection;
        }

        return null;
    }

    private static bool TableExists(DbConnection connection, string owner, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM all_tables
            WHERE owner = :owner
              AND table_name = :table_name
            """;

        var ownerParameter = command.CreateParameter();
        ownerParameter.ParameterName = "owner";
        ownerParameter.Value = owner;
        command.Parameters.Add(ownerParameter);

        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "table_name";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);

        var count = Convert.ToInt32(command.ExecuteScalar());

        return count > 0;
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

internal enum OracleNamingStyle
{
    None,
    NativeUppercase
}
