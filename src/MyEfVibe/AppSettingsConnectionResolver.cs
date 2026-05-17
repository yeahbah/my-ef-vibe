using System.Text.Json;

namespace MyEfVibe;

internal static class AppSettingsConnectionResolver
{
    internal static bool TryResolve(string outputDirectory, out string connectionString, out MyEfVibeProvider? provider)
    {
        connectionString = string.Empty;
        provider = null;

        foreach (var settingsFileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var settingsPath = Path.Combine(outputDirectory, settingsFileName);

            if (!File.Exists(settingsPath))
                continue;

            if (!TryReadConnectionString(settingsPath, out connectionString))
                continue;

            provider = InferProvider(outputDirectory, connectionString);

            return !string.IsNullOrWhiteSpace(connectionString);
        }

        return false;
    }

    private static bool TryReadConnectionString(string settingsPath, out string connectionString)
    {
        connectionString = string.Empty;

        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));

        if (!document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings))
            return false;

        foreach (var preferredName in new[] { "DefaultConnection", "Postgres", "Sqlite", "Database" })
        {
            if (connectionStrings.TryGetProperty(preferredName, out var named)
                && named.GetString() is { Length: > 0 } preferred)
            {
                connectionString = preferred;

                return true;
            }
        }

        foreach (var entry in connectionStrings.EnumerateObject())
        {
            if (entry.Value.GetString() is { Length: > 0 } first)
            {
                connectionString = first;

                return true;
            }
        }

        return false;
    }

    private static MyEfVibeProvider? InferProvider(string outputDirectory, string connectionString)
    {
        if (Directory.EnumerateFiles(outputDirectory, "Npgsql*.dll").Any())
            return MyEfVibeProvider.Npgsql;

        if (Directory.EnumerateFiles(outputDirectory, "Microsoft.Data.SqlClient*.dll").Any()
            || Directory.EnumerateFiles(outputDirectory, "Microsoft.EntityFrameworkCore.SqlServer*.dll").Any())
            return MyEfVibeProvider.SqlServer;

        if (Directory.EnumerateFiles(outputDirectory, "Microsoft.EntityFrameworkCore.Sqlite*.dll").Any())
            return MyEfVibeProvider.Sqlite;

        if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Username=", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.Npgsql;

        if (connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
            && connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.Sqlite;

        if (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.SqlServer;

        return null;
    }
}
