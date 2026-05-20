using System.Data;
using System.Data.Common;
using System.Reflection;
using Spectre.Console;

namespace MyEfVibe;

internal static class DbInfoReporter
{
    internal static async Task WriteAsync(
        object dbContext,
        WorkspaceHost host,
        CancellationToken cancellationToken = default)
    {
        var contextType = dbContext.GetType();
        var rows = new List<(string Key, string Value)>
        {
            ("DbContext", contextType.FullName ?? contextType.Name),
            ("EF project", FormatProjectPath(host.ProjectPath)),
        };

        if (!string.Equals(host.ProjectPath, host.StartupProjectPath, StringComparison.OrdinalIgnoreCase))
            rows.Add(("Startup project", FormatProjectPath(host.StartupProjectPath)));

        rows.Add(("Session directory", host.SessionDirectory));

        var efVersion = TryGetEfCoreVersion();

        if (efVersion is not null)
            rows.Add(("EF Core", efVersion));

        var database = contextType.GetProperty("Database")?.GetValue(dbContext);

        if (database is null)
        {
            rows.Add(("Database", "(not available)"));
            WritePanel(rows);
            return;
        }

        var providerName = database.GetType().GetProperty("ProviderName")?.GetValue(database) as string;

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            rows.Add(("Provider", FormatProviderDisplay(providerName)));
            rows.Add(("Provider name", providerName));
        }

        var commandTimeout = database.GetType().GetProperty("CommandTimeout")?.GetValue(database);

        if (commandTimeout is int timeoutSeconds)
            rows.Add(("Command timeout", timeoutSeconds <= 0 ? "default" : $"{timeoutSeconds}s"));

        var dbSetCount = CountDbSets(dbContext);

        rows.Add(("DbSets", dbSetCount.ToString()));

        if (!RelationalDatabaseFacadeInvoker.TryGetDbConnection(
                database,
                host.EnumerateLoadedAssemblies(),
                out var connection)
            || connection is not DbConnection dbConnection)
        {
            rows.Add(("Connection", "(not relational or unavailable)"));
            WritePanel(rows);
            return;
        }

        var openedHere = dbConnection.State != ConnectionState.Open;

        try
        {
            if (openedHere)
                await dbConnection.OpenAsync(cancellationToken);

            rows.Add(("Connection state", dbConnection.State.ToString()));
            rows.Add(("Data source", NullIfEmpty(dbConnection.DataSource)));
            rows.Add(("Database", NullIfEmpty(dbConnection.Database)));
            rows.Add(("Server version", await TryGetServerVersionAsync(dbConnection, providerName, cancellationToken)
                ?? "(unavailable)"));
            rows.Add(("Connection string", NullIfEmpty(dbConnection.ConnectionString)));
        }
        catch (Exception failure)
        {
            rows.Add(("Connection error", failure.Message));
        }
        finally
        {
            if (openedHere && dbConnection.State != ConnectionState.Closed)
                await dbConnection.CloseAsync();
        }

        WritePanel(rows);
    }

    private static void WritePanel(IReadOnlyList<(string Key, string Value)> rows)
    {
        var table = new Table().RoundedBorder().BorderColor(Color.Grey);
        table.AddColumn(new TableColumn("[grey]Property[/]").NoWrap());
        table.AddColumn("[grey]Value[/]");

        foreach (var (key, value) in rows)
            table.AddRow(Markup.Escape(key), Markup.Escape(value));

        AnsiConsole.Write(
            new Panel(table)
            {
                Header = new PanelHeader("[bold]Database info[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0),
            });

        AnsiConsole.WriteLine();
    }

    private static string FormatProjectPath(string absolutePath)
        => Path.GetFileName(absolutePath);

    private static string FormatProviderDisplay(string providerName)
    {
        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return "SQL Server";

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            return "PostgreSQL";

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return "SQLite";

        if (providerName.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
            return "Oracle";

        if (providerName.Contains("MariaDb", StringComparison.OrdinalIgnoreCase)
            || providerName.Contains("MariaDB", StringComparison.Ordinal))
            return "MariaDB";

        if (providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase)
            || providerName.Contains("MySQL", StringComparison.Ordinal))
            return "MySQL";

        return providerName;
    }

    private static string? TryGetEfCoreVersion()
    {
        var efAssembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        return efAssembly?.GetName().Version?.ToString(3);
    }

    private static int CountDbSets(object dbContext)
    {
        var count = 0;

        foreach (var property in dbContext.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            var propertyType = property.PropertyType;

            if (!propertyType.IsGenericType)
                continue;

            if (propertyType.GetGenericTypeDefinition().FullName?
                    .StartsWith("Microsoft.EntityFrameworkCore.DbSet`1", StringComparison.Ordinal) != true)
                continue;

            count++;
        }

        return count;
    }

    private static async Task<string?> TryGetServerVersionAsync(
        DbConnection connection,
        string? providerName,
        CancellationToken cancellationToken)
    {
        var sql = providerName switch
        {
            null => null,
            _ when providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) => "SELECT @@VERSION",
            _ when providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) => "SELECT version()",
            _ when providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) => "SELECT sqlite_version()",
            _ when providerName.Contains("Oracle", StringComparison.OrdinalIgnoreCase) =>
                "SELECT banner FROM v$version WHERE ROWNUM = 1",
            _ when providerName.Contains("MariaDb", StringComparison.OrdinalIgnoreCase)
                || providerName.Contains("MariaDB", StringComparison.Ordinal)
                || providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase)
                || providerName.Contains("MySQL", StringComparison.Ordinal) => "SELECT VERSION()",
            _ => null,
        };

        if (sql is null)
            return null;

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var scalar = await command.ExecuteScalarAsync(cancellationToken);

            return scalar?.ToString()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? "(empty)" : value;
}
