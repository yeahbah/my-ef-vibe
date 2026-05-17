using System.Data;
using System.Reflection;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace MyEfVibe;

internal static partial class QueryPlanRunner
{
    internal static async Task WritePlanAsync(
        object dbContext,
        string? translatedSql,
        IEnumerable<Assembly> inspectionAssemblies,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(translatedSql))
        {
            CliUi.WriteWarning("No translated SQL yet. Run a query that produces IQueryable SQL first.");
            return;
        }

        var provider = ResolveProvider(dbContext);
        var executableSql = SanitizeTranslatedSql(translatedSql);
        var explainSql = BuildExplainSql(provider, executableSql);

        if (explainSql is null)
        {
            CliUi.WriteWarning("EXPLAIN is not supported for this provider.");
            return;
        }

        try
        {
            var rows = await ExecuteQueryAsync(dbContext, explainSql, inspectionAssemblies, cancellationToken);

            if (rows.Count == 0)
            {
                CliUi.WriteWarning("EXPLAIN returned no rows.");
                return;
            }

            CliUi.WriteSqlBlock("Query plan", string.Join(Environment.NewLine, rows));
        }
        catch (Exception failure)
        {
            CliUi.WriteErrorPanel("Query plan failed", failure.Message);
        }
    }

    /// <summary>
    /// Strips EF <c>ToQueryString()</c> parameter comment lines and inlines parameter values.
    /// </summary>
    private static string SanitizeTranslatedSql(string sql)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var sqlLines = new List<string>();

        foreach (var line in sql.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("-- @", StringComparison.Ordinal))
            {
                var match = ParameterCommentRegex().Match(trimmed);

                if (match.Success)
                    parameters[match.Groups["name"].Value] = match.Groups["value"].Value;

                continue;
            }

            sqlLines.Add(line);
        }

        var body = string.Join(Environment.NewLine, sqlLines).Trim();

        foreach (var (name, value) in parameters)
            body = body.Replace($"@{name}", FormatParameterLiteral(value), StringComparison.Ordinal);

        return body;
    }

    private static string FormatParameterLiteral(string value)
    {
        if (long.TryParse(value, out _))
            return value;

        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            return value;

        if (bool.TryParse(value, out var boolean))
            return boolean ? "TRUE" : "FALSE";

        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    [GeneratedRegex(@"^-- @(?<name>\w+)='(?<value>.*)'$")]
    private static partial Regex ParameterCommentRegex();

    private static string? BuildExplainSql(MyEfVibeProvider? provider, string sql)
    {
        var trimmed = sql.Trim().TrimEnd(';');

        return provider switch
        {
            MyEfVibeProvider.Npgsql => $"EXPLAIN {trimmed}",
            MyEfVibeProvider.Sqlite => $"EXPLAIN QUERY PLAN {trimmed}",
            MyEfVibeProvider.SqlServer => $"SET SHOWPLAN_ALL ON;{Environment.NewLine}{trimmed}",
            _ => $"EXPLAIN {trimmed}",
        };
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

        return null;
    }

    private static async Task<IReadOnlyList<string>> ExecuteQueryAsync(
        object dbContext,
        string sql,
        IEnumerable<Assembly> inspectionAssemblies,
        CancellationToken cancellationToken)
    {
        var database = dbContext.GetType().GetProperty("Database")?.GetValue(dbContext)
            ?? throw new InvalidOperationException("Database facade not found.");

        if (!RelationalDatabaseFacadeInvoker.TryGetDbConnection(database, inspectionAssemblies, out var connection)
            || connection is null)
        {
            throw new InvalidOperationException(
                "Could not resolve EF Core GetDbConnection. Ensure Microsoft.EntityFrameworkCore.Relational is loaded.");
        }

        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
            await connection.OpenAsync(cancellationToken);

        try
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
        finally
        {
            if (openedHere && connection.State != ConnectionState.Closed)
                await connection.CloseAsync();
        }
    }
}
