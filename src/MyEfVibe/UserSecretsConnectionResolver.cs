using System.Text.Json;

namespace MyEfVibe;

internal static class UserSecretsConnectionResolver
{
    internal static bool TryResolve(
        string projectPath,
        string outputDirectory,
        out string connectionString,
        out MyEfVibeProvider? provider)
    {
        connectionString = string.Empty;
        provider = null;

        if (!CsprojInspector.TryGetUserSecretsId(projectPath, out var userSecretsId))
            return false;

        var secretsPath = ResolveSecretsFilePath(userSecretsId);

        if (secretsPath is null || !TryReadConnectionString(secretsPath, out connectionString))
            return false;

        provider = AppSettingsConnectionResolver.InferProvider(outputDirectory, connectionString);

        return !string.IsNullOrWhiteSpace(connectionString);
    }

    private static string? ResolveSecretsFilePath(string userSecretsId)
    {
        var secretsRoot = GetUserSecretsRootDirectory();

        if (string.IsNullOrEmpty(secretsRoot))
            return null;

        var candidate = Path.Combine(secretsRoot, userSecretsId, "secrets.json");

        return File.Exists(candidate) ? candidate : null;
    }

    private static string GetUserSecretsRootDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "UserSecrets");
        }

        var home = Environment.GetEnvironmentVariable("HOME")
                   ?? Environment.GetEnvironmentVariable("USERPROFILE");

        return string.IsNullOrWhiteSpace(home)
            ? string.Empty
            : Path.Combine(home, ".microsoft", "usersecrets");
    }

    private static bool TryReadConnectionString(string secretsPath, out string connectionString)
    {
        connectionString = string.Empty;

        using var document = JsonDocument.Parse(File.ReadAllText(secretsPath));

        foreach (var preferredName in ConnectionStringKeys.PreferredNames)
        {
            var flatKey = ConnectionStringKeys.FlatKey(preferredName);

            if (document.RootElement.TryGetProperty(flatKey, out var named)
                && named.GetString() is { Length: > 0 } preferred)
            {
                connectionString = preferred;

                return true;
            }
        }

        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (!entry.Name.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.Value.GetString() is { Length: > 0 } first)
            {
                connectionString = first;

                return true;
            }
        }

        return false;
    }
}
