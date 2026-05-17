using System.Text.Json;

namespace MyEfVibe;

internal static class AppSettingsConnectionResolver
{
    internal static bool TryResolve(
        string startupProjectPath,
        string efOutputDirectory,
        out string connectionString,
        out MyEfVibeProvider? provider)
    {
        connectionString = string.Empty;
        provider = null;

        if (UserSecretsConnectionResolver.TryResolve(startupProjectPath, efOutputDirectory, out connectionString, out provider))
            return true;

        foreach (var settingsPath in EnumerateStartupSettingsPaths(startupProjectPath))
        {
            if (!TryReadConnectionString(settingsPath, out connectionString))
                continue;

            provider = InferProvider(efOutputDirectory, connectionString);

            return !string.IsNullOrWhiteSpace(connectionString);
        }

        return false;
    }

    private static IEnumerable<string> EnumerateStartupSettingsPaths(string startupProjectPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var startupDirectory = Path.GetDirectoryName(startupProjectPath)!;

        foreach (var settingsFileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            foreach (var directory in EnumerateStartupSearchDirectories(startupDirectory))
            {
                var candidate = Path.Combine(directory, settingsFileName);

                if (seen.Add(candidate) && File.Exists(candidate))
                    yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateStartupSearchDirectories(string startupProjectDirectory)
    {
        yield return startupProjectDirectory;

        var binRoot = Path.Combine(startupProjectDirectory, "bin");

        if (Directory.Exists(binRoot))
        {
            foreach (var configuration in new[] { "Release", "Debug" })
            {
                var configurationRoot = Path.Combine(binRoot, configuration);

                if (!Directory.Exists(configurationRoot))
                    continue;

                foreach (var tfmFolder in Directory.EnumerateDirectories(configurationRoot))
                    yield return tfmFolder;
            }
        }

        var current = startupProjectDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var parent = Directory.GetParent(current)?.FullName;

            if (string.IsNullOrEmpty(parent))
                break;

            current = parent;
            yield return current;
        }
    }

    private static bool TryReadConnectionString(string settingsPath, out string connectionString)
    {
        connectionString = string.Empty;

        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));

        if (!document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings))
            return false;

        foreach (var preferredName in ConnectionStringKeys.PreferredNames)
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

    internal static MyEfVibeProvider? InferProvider(string outputDirectory, string connectionString)
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
