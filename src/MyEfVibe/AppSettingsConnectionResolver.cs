using System.Text.Json;

namespace MyEfVibe;

internal static class AppSettingsConnectionResolver
{
    internal static bool TryResolve(
        string outputDirectory,
        string workspaceDirectory,
        out string connectionString,
        out MyEfVibeProvider? provider)
    {
        connectionString = string.Empty;
        provider = null;

        foreach (var settingsPath in EnumerateCandidateSettingsPaths(outputDirectory, workspaceDirectory))
        {
            if (!TryReadConnectionString(settingsPath, out connectionString))
                continue;

            provider = InferProvider(outputDirectory, connectionString);

            return !string.IsNullOrWhiteSpace(connectionString);
        }

        return false;
    }

    private static IEnumerable<string> EnumerateCandidateSettingsPaths(
        string outputDirectory,
        string workspaceDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var settingsFileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            foreach (var directory in EnumerateSearchDirectories(outputDirectory, workspaceDirectory))
            {
                var candidate = Path.Combine(directory, settingsFileName);

                if (seen.Add(candidate) && File.Exists(candidate))
                    yield return candidate;
            }

            if (!Directory.Exists(workspaceDirectory))
                continue;

            foreach (var candidate in Directory.EnumerateFiles(
                         workspaceDirectory,
                         settingsFileName,
                         SearchOption.AllDirectories))
            {
                if (!seen.Add(candidate))
                    continue;

                if (IsUnderBuildArtifacts(candidate))
                    continue;

                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateSearchDirectories(string outputDirectory, string workspaceDirectory)
    {
        yield return outputDirectory;

        var current = outputDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var parent = Directory.GetParent(current)?.FullName;

            if (string.IsNullOrEmpty(parent))
                break;

            current = parent;
            yield return current;

            if (string.Equals(
                    Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(workspaceDirectory).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                break;
        }
    }

    private static bool IsUnderBuildArtifacts(string absolutePath)
    {
        foreach (var segment in absolutePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase))
                return true;
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
