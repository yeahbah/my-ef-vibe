using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe;

internal static class DbInfoJsonReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    internal static async Task WriteAsync(
        object dbContext,
        WorkspaceHost host,
        CancellationToken cancellationToken = default)
    {
        var payload = await BuildAsync(dbContext, host, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    internal static async Task<DbInfoJsonPayload> BuildAsync(
        object dbContext,
        WorkspaceHost host,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<DbInfoJsonEntry>();
        var contextType = dbContext.GetType();

        rows.Add(new DbInfoJsonEntry { Key = "DbContext", Value = contextType.FullName ?? contextType.Name });
        rows.Add(new DbInfoJsonEntry { Key = "EF project", Value = host.ProjectPath });

        if (!string.Equals(host.ProjectPath, host.StartupProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            rows.Add(new DbInfoJsonEntry { Key = "Startup project", Value = host.StartupProjectPath });
        }

        rows.Add(new DbInfoJsonEntry { Key = "Session directory", Value = host.SessionDirectory });

        var efVersion = TryGetEfCoreVersion();

        if (efVersion is not null)
        {
            rows.Add(new DbInfoJsonEntry { Key = "EF Core", Value = efVersion });
        }

        var database = contextType.GetProperty("Database")?.GetValue(dbContext);

        if (database is null)
        {
            rows.Add(new DbInfoJsonEntry { Key = "Database", Value = "(not available)" });

            return new DbInfoJsonPayload
            {
                DbContext = contextType.Name,
                Entries = rows.ToArray()
            };
        }

        var providerName = database.GetType().GetProperty("ProviderName")?.GetValue(database) as string;

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            rows.Add(new DbInfoJsonEntry
                { Key = "Provider", Value = DbInfoReporter.FormatProviderDisplay(providerName) });
            rows.Add(new DbInfoJsonEntry { Key = "Provider name", Value = providerName });
        }

        var commandTimeout = database.GetType().GetProperty("CommandTimeout")?.GetValue(database);
        var timeoutSeconds = commandTimeout is int timeout ? timeout : 0;
        rows.Add(new DbInfoJsonEntry
        {
            Key = "Command timeout",
            Value = timeoutSeconds <= 0 ? "default" : $"{timeoutSeconds}s"
        });

        rows.Add(new DbInfoJsonEntry
        {
            Key = "DbSets",
            Value = SchemaBrowser.GetDbSets(dbContext).Count.ToString()
        });

        if (RelationalDatabaseFacadeInvoker.TryGetDbConnection(
                database,
                host.EnumerateLoadedAssemblies(),
                out var connection)
            && connection is DbConnection dbConnection)
        {
            try
            {
                rows.Add(new DbInfoJsonEntry { Key = "Connection state", Value = dbConnection.State.ToString() });
                rows.Add(new DbInfoJsonEntry { Key = "Data source", Value = NullIfEmpty(dbConnection.DataSource) });
                rows.Add(new DbInfoJsonEntry { Key = "Database", Value = NullIfEmpty(dbConnection.Database) });
                rows.Add(new DbInfoJsonEntry
                {
                    Key = "Server version",
                    Value = await DbInfoReporter.TryGetServerVersionAsync(dbConnection, providerName, cancellationToken)
                            ?? "(unknown)"
                });
                rows.Add(new DbInfoJsonEntry
                    { Key = "Connection string", Value = NullIfEmpty(dbConnection.ConnectionString) });
            }
            catch (Exception failure)
            {
                rows.Add(new DbInfoJsonEntry { Key = "Connection error", Value = failure.Message });
            }
        }
        else
        {
            rows.Add(new DbInfoJsonEntry { Key = "Connection", Value = "(not relational or unavailable)" });
        }

        return new DbInfoJsonPayload
        {
            DbContext = contextType.Name,
            Entries = rows.ToArray()
        };
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? TryGetEfCoreVersion()
    {
        var efAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        return efAssembly?.GetName().Version?.ToString(3);
    }

    internal sealed class DbInfoJsonPayload
    {
        public string DbContext { get; init; } = string.Empty;

        public DbInfoJsonEntry[] Entries { get; init; } = Array.Empty<DbInfoJsonEntry>();
    }

    internal sealed class DbInfoJsonEntry
    {
        public string Key { get; init; } = string.Empty;

        public string? Value { get; init; }
    }
}