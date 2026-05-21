using System.Data;
using System.Reflection;

namespace MyEfVibe.IntegrationTests;

internal static class DatabaseProbe
{
    internal static bool TryValidateScenario(IntegrationScenario scenario, out string? failureReason)
    {
        failureReason = null;

        if (!Directory.Exists(scenario.RepoRoot))
        {
            failureReason = $"Repo root not found: {scenario.RepoRoot}";
            return false;
        }

        if (!File.Exists(scenario.EfProjectPath))
        {
            failureReason = $"EF project not found: {scenario.EfProjectPath}";
            return false;
        }

        if (!File.Exists(scenario.StartupProjectPath))
        {
            failureReason = $"Startup project not found: {scenario.StartupProjectPath}";
            return false;
        }

        return true;
    }

    internal static async Task<bool> CanConnectAsync(
        object dbContext,
        ScriptSession scriptSession,
        WorkspaceHost host,
        CancellationToken cancellationToken = default)
    {
        var database = dbContext.GetType().GetProperty("Database")?.GetValue(dbContext);

        if (database is null)
            return false;

        if (RelationalDatabaseFacadeInvoker.TryGetDbConnection(
                database,
                host.EnumerateLoadedAssemblies(),
                out var connection)
            && connection is not null)
        {
            var openedHere = connection.State != ConnectionState.Open;

            try
            {
                if (openedHere)
                    await connection.OpenAsync(cancellationToken);

                return connection.State == ConnectionState.Open;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (openedHere && connection.State != ConnectionState.Closed)
                    await connection.CloseAsync();
            }
        }

        return await CanConnectViaQueryAsync(dbContext, scriptSession, host, cancellationToken);
    }

    private static async Task<bool> CanConnectViaQueryAsync(
        object dbContext,
        ScriptSession scriptSession,
        WorkspaceHost host,
        CancellationToken cancellationToken)
    {
        try
        {
            var (_, metrics) = await QueryEvaluator.EvaluateAsync(
                dbContext,
                scriptSession,
                "db.Products.Take(1).ToList();",
                new DbLogSettings { Enabled = true },
                host.EnumerateLoadedAssemblies(),
                cancellationToken);

            return metrics.Succeeded && metrics.RowCount is > 0;
        }
        catch
        {
            return false;
        }
    }

    internal static string ReadProviderName(object dbContext)
    {
        var database = dbContext.GetType().GetProperty("Database")?.GetValue(dbContext);

        return database?.GetType().GetProperty("ProviderName")?.GetValue(database) as string
               ?? string.Empty;
    }

    internal static bool ProviderMatches(IntegrationScenario scenario, string providerName) =>
        scenario.Provider switch
        {
            "sqlserver" => providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase),
            "npgsql" => providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase),
            "oracle" => providerName.Contains("Oracle", StringComparison.OrdinalIgnoreCase),
            "sqlite" => providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
}
