using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace MyEfVibe;

internal static partial class QueryPlanRunner
{
    internal static string SanitizeSqlForExplain(string sql, MyEfVibeProvider? provider)
        => SanitizeTranslatedSql(sql, provider);

    internal static async Task<QueryPlanResult> TryExplainAsync(
        object dbContext,
        string? sql,
        IEnumerable<Assembly> inspectionAssemblies,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return QueryPlanResult.Failed("No SQL available for EXPLAIN.");

        var provider = ResolveProvider(dbContext);

        if (provider is null)
            return QueryPlanResult.Failed("EXPLAIN is not supported for this provider.");

        try
        {
            var executableSql = SanitizeTranslatedSql(sql, provider);

            var rows = provider switch
            {
                MyEfVibeProvider.SqlServer => await ExecuteSqlServerShowPlanAsync(
                    dbContext,
                    executableSql,
                    inspectionAssemblies,
                    cancellationToken),
                MyEfVibeProvider.Oracle => await ExecuteOracleExplainPlanAsync(
                    dbContext,
                    executableSql,
                    inspectionAssemblies,
                    cancellationToken),
                _ => await ExecuteQueryAsync(
                    dbContext,
                    BuildExplainSql(provider.Value, executableSql),
                    inspectionAssemblies,
                    cancellationToken),
            };

            if (rows.Count == 0)
                return QueryPlanResult.Failed("EXPLAIN returned no rows.");

            return QueryPlanResult.Succeeded(rows);
        }
        catch (Exception failure)
        {
            return QueryPlanResult.Failed(failure.Message);
        }
    }

    internal static async Task WritePlanAsync(
        object dbContext,
        string? translatedSql,
        IEnumerable<Assembly> inspectionAssemblies,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(translatedSql))
        {
            CliUi.WriteWarning(
                "No SQL available yet. Run a query with :dblog on (executed SQL), "
                + "or an IQueryable expression (uses ToQueryString()).");
            return;
        }

        var result = await TryExplainAsync(dbContext, translatedSql, inspectionAssemblies, cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.PlanText))
        {
            CliUi.WriteSqlBlock("Query plan", result.PlanText);
            return;
        }

        CliUi.WriteErrorPanel("Query plan failed", result.Note ?? "EXPLAIN failed.");
    }

    /// <summary>
    /// Strips parameter/duration comment lines from captured or translated SQL and inlines parameter values.
    /// </summary>
    private static string SanitizeTranslatedSql(string sql, MyEfVibeProvider? provider)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectParameterAssignments(sql, parameters);

        var executable = DbLogSqlExtractor.ExtractExecutableSql(sql) ?? sql.Trim();

        if (provider == MyEfVibeProvider.Oracle)
        {
            var oracleSql = OracleSqlExtractor.TryExtractExplainableSql(executable);

            if (!string.IsNullOrWhiteSpace(oracleSql))
                executable = oracleSql;
        }

        var body = executable;

        foreach (var name in parameters.Keys.OrderByDescending(static n => n.Length))
            body = InlineParameter(body, name, parameters[name]);

        return body;
    }

    private static void CollectParameterAssignments(string sql, Dictionary<string, string> parameters)
    {
        foreach (var line in sql.Split('\n'))
        {
            var trimmed = line.Trim();

            if (TryParseToQueryStringParameterComment(trimmed, parameters))
                continue;

            TryParseDbLogParametersComment(trimmed, parameters);
        }
    }

    private static string InlineParameter(string sql, string name, string value)
        => sql.Replace($"@{name}", FormatParameterLiteral(value), StringComparison.Ordinal);

    private static bool TryParseToQueryStringParameterComment(string trimmed, Dictionary<string, string> parameters)
    {
        if (!trimmed.StartsWith("-- @", StringComparison.Ordinal))
            return false;

        var match = ParameterCommentRegex().Match(trimmed);

        if (!match.Success)
            return false;

        parameters[match.Groups["name"].Value] = match.Groups["value"].Value;
        return true;
    }

    private static bool TryParseDbLogParametersComment(string trimmed, Dictionary<string, string> parameters)
    {
        const string prefix = "-- parameters:";

        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var assignments = trimmed[prefix.Length..];

        foreach (Match match in DbLogParameterAssignmentRegex().Matches(assignments))
        {
            var parameterName = match.Groups["name"].Value.Trim();

            if (parameterName.Length == 0)
                continue;

            parameters[parameterName] = match.Groups["value"].Value.Trim();
        }

        return true;
    }

    private static string FormatParameterLiteral(string value)
    {
        if (string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase))
            return "NULL";

        if (long.TryParse(value, out _))
            return value;

        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            return value;

        if (bool.TryParse(value, out var boolean))
            return boolean ? "1" : "0";

        if (value.StartsWith('\'') && value.EndsWith('\'') && value.Length >= 2)
            return value;

        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    [GeneratedRegex(@"^-- @(?<name>\w+)='(?<value>.*)'$")]
    private static partial Regex ParameterCommentRegex();

    [GeneratedRegex(@"@(?<name>\w+)\s*=\s*(?<value>[^,]+)")]
    private static partial Regex DbLogParameterAssignmentRegex();

    private static string BuildExplainSql(MyEfVibeProvider provider, string sql)
    {
        var trimmed = sql.Trim().TrimEnd(';');

        return provider switch
        {
            MyEfVibeProvider.Npgsql => $"EXPLAIN {trimmed}",
            MyEfVibeProvider.Sqlite => $"EXPLAIN QUERY PLAN {trimmed}",
            MyEfVibeProvider.MySql or MyEfVibeProvider.MariaDb => $"EXPLAIN {trimmed}",
            MyEfVibeProvider.Oracle => $"EXPLAIN PLAN FOR {trimmed}",
            _ => $"EXPLAIN {trimmed}",
        };
    }

    private static async Task<IReadOnlyList<string>> ExecuteOracleExplainPlanAsync(
        object dbContext,
        string sql,
        IEnumerable<Assembly> inspectionAssemblies,
        CancellationToken cancellationToken)
    {
        await using var scope = await OpenConnectionScopeAsync(dbContext, inspectionAssemblies, cancellationToken);

        await ExecuteNonQueryAsync(scope.Connection, $"EXPLAIN PLAN FOR {sql}", cancellationToken);

        return await ReadQueryRowsAsync(
            scope.Connection,
            "SELECT PLAN_TABLE_OUTPUT FROM TABLE(DBMS_XPLAN.DISPLAY(NULL, NULL, 'BASIC'))",
            cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ExecuteSqlServerShowPlanAsync(
        object dbContext,
        string sql,
        IEnumerable<Assembly> inspectionAssemblies,
        CancellationToken cancellationToken)
    {
        await using var scope = await OpenConnectionScopeAsync(dbContext, inspectionAssemblies, cancellationToken);

        await ExecuteNonQueryAsync(scope.Connection, "SET SHOWPLAN_ALL ON", cancellationToken);

        try
        {
            return await ReadQueryRowsAsync(scope.Connection, sql, cancellationToken);
        }
        finally
        {
            await ExecuteNonQueryAsync(scope.Connection, "SET SHOWPLAN_ALL OFF", cancellationToken);
        }
    }

    private static MyEfVibeProvider? ResolveProvider(object dbContext)
    {
        var database = dbContext.GetType().GetProperty("Database")?.GetValue(dbContext);

        if (database is null)
            return null;

        var providerName = database.GetType().GetProperty("ProviderName")?.GetValue(database) as string;

        if (string.IsNullOrWhiteSpace(providerName))
            return null;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.Npgsql;

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.Sqlite;

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.SqlServer;

        if (providerName.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.Oracle;

        if (providerName.Contains("MariaDb", StringComparison.OrdinalIgnoreCase)
            || providerName.Contains("MariaDB", StringComparison.Ordinal))
            return MyEfVibeProvider.MariaDb;

        if (providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase)
            || providerName.Contains("MySQL", StringComparison.Ordinal))
            return MyEfVibeProvider.MySql;

        return null;
    }

    private static async Task<IReadOnlyList<string>> ExecuteQueryAsync(
        object dbContext,
        string sql,
        IEnumerable<Assembly> inspectionAssemblies,
        CancellationToken cancellationToken)
    {
        await using var scope = await OpenConnectionScopeAsync(dbContext, inspectionAssemblies, cancellationToken);
        return await ReadQueryRowsAsync(scope.Connection, sql, cancellationToken);
    }

    private static async Task<ConnectionScope> OpenConnectionScopeAsync(
        object dbContext,
        IEnumerable<Assembly> inspectionAssemblies,
        CancellationToken cancellationToken)
    {
        var database = dbContext.GetType().GetProperty("Database")?.GetValue(dbContext)
            ?? throw new InvalidOperationException("Database facade not found.");

        if (!RelationalDatabaseFacadeInvoker.TryGetDbConnection(database, inspectionAssemblies, out var connection)
            || connection is not DbConnection dbConnection)
        {
            throw new InvalidOperationException(
                "Could not resolve EF Core GetDbConnection. Ensure Microsoft.EntityFrameworkCore.Relational is loaded.");
        }

        var openedHere = dbConnection.State != ConnectionState.Open;

        if (openedHere)
            await dbConnection.OpenAsync(cancellationToken);

        return new ConnectionScope(dbConnection, openedHere);
    }

    private static async Task<IReadOnlyList<string>> ReadQueryRowsAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new string[reader.FieldCount];

            for (var column = 0; column < reader.FieldCount; column++)
                values[column] = reader.GetValue(column)?.ToString() ?? string.Empty;

            rows.Add(string.Join(" | ", values));
        }

        return rows;
    }

    private static async Task ExecuteNonQueryAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class ConnectionScope : IAsyncDisposable
    {
        internal ConnectionScope(DbConnection connection, bool openedHere)
        {
            Connection = connection;
            _openedHere = openedHere;
        }

        internal DbConnection Connection { get; }

        private readonly bool _openedHere;

        public async ValueTask DisposeAsync()
        {
            if (_openedHere && Connection.State != ConnectionState.Closed)
                await Connection.CloseAsync();
        }
    }
}
