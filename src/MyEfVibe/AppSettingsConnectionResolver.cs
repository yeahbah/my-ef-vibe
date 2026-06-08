using System.Text.Json;

namespace MyEfVibe;

internal static class AppSettingsConnectionResolver
{
    internal static bool TryResolve(
        string startupProjectPath,
        string efProjectPath,
        string efOutputDirectory,
        out string connectionString,
        out ProviderDescriptor? providerDescriptor)
    {
        connectionString = string.Empty;
        providerDescriptor = null;

        if (!TryResolveLayeredAppSettings(startupProjectPath, out connectionString))
        {
            foreach (var settingsPath in EnumerateStartupSettingsPaths(startupProjectPath))
            {
                if (!TryReadConnectionString(settingsPath, out connectionString))
                {
                    continue;
                }

                break;
            }
        }

        if (UserSecretsConnectionResolver.TryResolve(
                startupProjectPath,
                out var secretsConnectionString))
        {
            connectionString = secretsConnectionString;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        providerDescriptor = ResolveProviderDescriptor(startupProjectPath, efProjectPath);

        if (providerDescriptor?.IsSqlite == true)
        {
            connectionString = SqliteConnectionStringNormalizer.Normalize(
                connectionString,
                startupProjectPath,
                efOutputDirectory);
        }

        return true;
    }

    internal static ProviderDescriptor? ResolveProviderDescriptor(string startupProjectPath, string efProjectPath)
    {
        var fromSettings = MapDatabaseProviderName(TryReadDatabaseProviderName(startupProjectPath));

        if (fromSettings.HasValue)
        {
            return ProviderDescriptor.FromKnownProvider(fromSettings.Value);
        }

        return CsprojInspector.TryReadEntityFrameworkProviderDescriptor(efProjectPath);
    }

    internal static MyEfVibeProvider? ResolveProvider(string startupProjectPath, string efProjectPath)
    {
        return ResolveProviderDescriptor(startupProjectPath, efProjectPath)?.KnownProvider;
    }

    internal static string? TryReadDatabaseProviderName(string startupProjectPath)
    {
        if (string.IsNullOrWhiteSpace(startupProjectPath))
        {
            return null;
        }

        var startupDirectory = Path.GetDirectoryName(startupProjectPath)!;
        string? databaseProvider = null;

        foreach (var settingsFileName in EnumerateSettingsFileNames())
        {
            var settingsPath = Path.Combine(startupDirectory, settingsFileName);

            if (TryReadDatabaseProvider(settingsPath, out var candidate))
            {
                databaseProvider = candidate;
            }
        }

        return databaseProvider;
    }

    private static bool TryResolveLayeredAppSettings(string startupProjectPath, out string connectionString)
    {
        connectionString = string.Empty;
        var startupDirectory = Path.GetDirectoryName(startupProjectPath)!;

        foreach (var settingsFileName in EnumerateSettingsFileNames())
        {
            var settingsPath = Path.Combine(startupDirectory, settingsFileName);

            if (TryReadConnectionString(settingsPath, out var candidate))
            {
                connectionString = candidate;
            }
        }

        return !string.IsNullOrWhiteSpace(connectionString);
    }

    private static bool TryReadDatabaseProvider(string settingsPath, out string? databaseProvider)
    {
        databaseProvider = null;

        if (!ConfigurationJson.TryParseFile(settingsPath, out var document) || document is null)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.TryGetProperty("EntityFrameworkCoreSettings", out var section)
                && section.TryGetProperty("DatabaseProvider", out var providerElement))
            {
                databaseProvider = providerElement.GetString();

                return !string.IsNullOrWhiteSpace(databaseProvider);
            }

            if (document.RootElement.TryGetProperty("Database", out var databaseSection)
                && databaseSection.TryGetProperty("Provider", out var databaseProviderElement))
            {
                databaseProvider = databaseProviderElement.GetString();

                return !string.IsNullOrWhiteSpace(databaseProvider);
            }

            return false;
        }
    }

    internal static MyEfVibeProvider? MapDatabaseProviderName(string? databaseProvider)
    {
        if (string.IsNullOrWhiteSpace(databaseProvider))
        {
            return null;
        }

        if (databaseProvider.Contains("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return MyEfVibeProvider.Npgsql;
        }

        if (databaseProvider.Contains("mysql", StringComparison.OrdinalIgnoreCase))
        {
            return MyEfVibeProvider.MySql;
        }

        if (databaseProvider.Contains("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return MyEfVibeProvider.MariaDb;
        }

        if (databaseProvider.Contains("oracle", StringComparison.OrdinalIgnoreCase))
        {
            return MyEfVibeProvider.Oracle;
        }

        if (databaseProvider.Contains("sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return MyEfVibeProvider.Sqlite;
        }

        if (databaseProvider.Contains("sql", StringComparison.OrdinalIgnoreCase))
        {
            return MyEfVibeProvider.SqlServer;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateStartupSettingsPaths(string startupProjectPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var startupDirectory = Path.GetDirectoryName(startupProjectPath)!;

        foreach (var settingsFileName in EnumerateSettingsFileNames())
        {
            foreach (var directory in EnumerateStartupSearchDirectories(startupDirectory))
            {
                var candidate = Path.Combine(directory, settingsFileName);

                if (seen.Add(candidate) && File.Exists(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateSettingsFileNames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in EnumerateSettingsFileNamesCore())
        {
            if (seen.Add(fileName))
            {
                yield return fileName;
            }
        }
    }

    private static IEnumerable<string> EnumerateSettingsFileNamesCore()
    {
        yield return "appsettings.json";
        yield return "appsettings.Development.json";

        var environmentName = ResolveEnvironmentName();

        if (!string.IsNullOrWhiteSpace(environmentName)
            && !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"appsettings.{environmentName}.json";
        }
    }

    private static string? ResolveEnvironmentName()
    {
        foreach (var variableName in new[]
                 {
                     "ASPNETCORE_ENVIRONMENT",
                     "DOTNET_ENVIRONMENT",
                     "APPSETTING_ASPNETCORE_ENVIRONMENT"
                 })
        {
            var value = Environment.GetEnvironmentVariable(variableName);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
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
                {
                    continue;
                }

                foreach (var tfmFolder in Directory.EnumerateDirectories(configurationRoot))
                {
                    yield return tfmFolder;
                }
            }
        }

        var current = startupProjectDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var parent = Directory.GetParent(current)?.FullName;

            if (string.IsNullOrEmpty(parent))
            {
                break;
            }

            current = parent;
            yield return current;
        }
    }

    private static bool TryReadConnectionString(string settingsPath, out string connectionString)
    {
        connectionString = string.Empty;

        if (!ConfigurationJson.TryParseFile(settingsPath, out var document) || document is null)
        {
            return false;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings))
            {
                return false;
            }

            return TryReadConnectionStringsSection(connectionStrings, out connectionString);
        }
    }

    private static bool TryReadConnectionStringsSection(JsonElement connectionStrings, out string connectionString)
    {
        connectionString = string.Empty;

        if (connectionStrings.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

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
}
