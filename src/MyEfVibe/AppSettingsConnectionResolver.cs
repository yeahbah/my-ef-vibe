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

        if (!TryResolveLayeredAppSettings(startupProjectPath, out connectionString))
        {
            foreach (var settingsPath in EnumerateStartupSettingsPaths(startupProjectPath))
            {
                if (!TryReadConnectionString(settingsPath, out connectionString))
                    continue;

                break;
            }
        }

        if (UserSecretsConnectionResolver.TryResolve(
                startupProjectPath,
                efOutputDirectory,
                out var secretsConnectionString,
                out var secretsProvider))
        {
            connectionString = secretsConnectionString;
            provider = secretsProvider;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        provider ??= InferProvider(efOutputDirectory, connectionString);

        if (provider == MyEfVibeProvider.Sqlite
            || SqliteConnectionStringNormalizer.LooksLikeSqliteConnection(connectionString))
        {
            connectionString = SqliteConnectionStringNormalizer.Normalize(
                connectionString,
                startupProjectPath,
                efOutputDirectory);
            provider ??= MyEfVibeProvider.Sqlite;
        }

        return true;
    }

    private static bool TryResolveLayeredAppSettings(string startupProjectPath, out string connectionString)
    {
        connectionString = string.Empty;
        var startupDirectory = Path.GetDirectoryName(startupProjectPath)!;

        foreach (var settingsFileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var settingsPath = Path.Combine(startupDirectory, settingsFileName);

            if (TryReadConnectionString(settingsPath, out var candidate))
                connectionString = candidate;
        }

        return !string.IsNullOrWhiteSpace(connectionString);
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

        if (!ConfigurationJson.TryParseFile(settingsPath, out var document) || document is null)
            return false;

        using (document)
        {
            if (!document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings))
                return false;

            return TryReadConnectionStringsSection(connectionStrings, out connectionString);
        }
    }

    private static bool TryReadConnectionStringsSection(JsonElement connectionStrings, out string connectionString)
    {
        connectionString = string.Empty;

        if (connectionStrings.ValueKind != JsonValueKind.Object)
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
        if (Directory.Exists(outputDirectory))
        {
            var fromArtifacts = TryInferProviderFromDepsJson(outputDirectory, connectionString)
                                ?? TryInferProviderFromOutputDlls(outputDirectory, connectionString);

            if (fromArtifacts.HasValue)
                return fromArtifacts;
        }

        return InferProviderFromConnectionString(connectionString);
    }

    private static MyEfVibeProvider? TryInferProviderFromOutputDlls(
        string outputDirectory,
        string connectionString)
    {
        if (Directory.EnumerateFiles(outputDirectory, "Npgsql*.dll").Any())
            return MyEfVibeProvider.Npgsql;

        if (Directory.EnumerateFiles(outputDirectory, "Pomelo.EntityFrameworkCore.MySql*.dll").Any()
            || Directory.EnumerateFiles(outputDirectory, "MySql.EntityFrameworkCore*.dll").Any())
        {
            return LooksLikeMariaDbConnection(connectionString)
                ? MyEfVibeProvider.MariaDb
                : MyEfVibeProvider.MySql;
        }

        if (Directory.EnumerateFiles(outputDirectory, "Microsoft.Data.SqlClient*.dll").Any()
            || Directory.EnumerateFiles(outputDirectory, "Microsoft.EntityFrameworkCore.SqlServer*.dll").Any())
            return MyEfVibeProvider.SqlServer;

        if (Directory.EnumerateFiles(outputDirectory, "Microsoft.EntityFrameworkCore.Sqlite*.dll").Any())
            return MyEfVibeProvider.Sqlite;

        if (Directory.EnumerateFiles(outputDirectory, "Oracle.EntityFrameworkCore*.dll").Any()
            || Directory.EnumerateFiles(outputDirectory, "Oracle.ManagedDataAccess*.dll").Any())
            return MyEfVibeProvider.Oracle;

        return null;
    }

    private static MyEfVibeProvider? TryInferProviderFromDepsJson(
        string outputDirectory,
        string connectionString)
    {
        foreach (var depsPath in Directory.EnumerateFiles(outputDirectory, "*.deps.json"))
        {
            string text;

            try
            {
                text = File.ReadAllText(depsPath);
            }
            catch (IOException)
            {
                continue;
            }

            if (DepsReferencesPackage(text, "Pomelo.EntityFrameworkCore.MySql")
                || DepsReferencesPackage(text, "MySql.EntityFrameworkCore"))
            {
                return LooksLikeMariaDbConnection(connectionString)
                    ? MyEfVibeProvider.MariaDb
                    : MyEfVibeProvider.MySql;
            }

            if (DepsReferencesPackage(text, "Microsoft.EntityFrameworkCore.SqlServer"))
                return MyEfVibeProvider.SqlServer;

            if (DepsReferencesPackage(text, "Npgsql.EntityFrameworkCore.PostgreSQL"))
                return MyEfVibeProvider.Npgsql;

            if (DepsReferencesPackage(text, "Microsoft.EntityFrameworkCore.Sqlite"))
                return MyEfVibeProvider.Sqlite;

            if (DepsReferencesPackage(text, "Oracle.EntityFrameworkCore"))
                return MyEfVibeProvider.Oracle;
        }

        return null;
    }

    private static bool DepsReferencesPackage(string depsJson, string packageName) =>
        depsJson.Contains($"\"{packageName}/", StringComparison.Ordinal);

    private static MyEfVibeProvider? InferProviderFromConnectionString(string connectionString)
    {
        if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Username=", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.Npgsql;

        if (connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
            && connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.Sqlite;

        if (LooksLikeMySqlConnection(connectionString))
        {
            return LooksLikeMariaDbConnection(connectionString)
                ? MyEfVibeProvider.MariaDb
                : MyEfVibeProvider.MySql;
        }

        if (connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase)
            || (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
                && connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase)
                && !connectionString.Contains("Port=3306", StringComparison.OrdinalIgnoreCase)))
            return MyEfVibeProvider.SqlServer;

        if (connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase)
            && connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.Oracle;

        return null;
    }

    internal static bool LooksLikeMySqlConnection(string connectionString) =>
        connectionString.Contains("Port=3306", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("Uid=", StringComparison.OrdinalIgnoreCase)
        || (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            && connectionString.Contains("User=", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeMariaDbConnection(string connectionString) =>
        connectionString.Contains("mariadb", StringComparison.OrdinalIgnoreCase);
}
