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
}

internal enum OracleNamingStyle
{
    None,
    NativeUppercase
}
