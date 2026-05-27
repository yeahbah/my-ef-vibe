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

        provider ??= MapDatabaseProviderName(TryReadDatabaseProviderName(startupProjectPath));
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

    internal static string? TryReadDatabaseProviderName(string startupProjectPath)
    {
        if (string.IsNullOrWhiteSpace(startupProjectPath))
            return null;

        var startupDirectory = Path.GetDirectoryName(startupProjectPath)!;
        string? databaseProvider = null;

        foreach (var settingsFileName in EnumerateSettingsFileNames())
        {
            var settingsPath = Path.Combine(startupDirectory, settingsFileName);

            if (TryReadDatabaseProvider(settingsPath, out var candidate))
                databaseProvider = candidate;
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
                connectionString = candidate;
        }

        return !string.IsNullOrWhiteSpace(connectionString);
    }

    private static bool TryReadDatabaseProvider(string settingsPath, out string? databaseProvider)
    {
        databaseProvider = null;

        if (!ConfigurationJson.TryParseFile(settingsPath, out var document) || document is null)
            return false;

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

    private static MyEfVibeProvider? MapDatabaseProviderName(string? databaseProvider)
    {
        if (string.IsNullOrWhiteSpace(databaseProvider))
            return null;

        if (databaseProvider.Contains("postgres", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.Npgsql;

        if (databaseProvider.Contains("mysql", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.MySql;

        if (databaseProvider.Contains("mariadb", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.MariaDb;

        if (databaseProvider.Contains("oracle", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.Oracle;

        if (databaseProvider.Contains("sqlite", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.Sqlite;

        if (databaseProvider.Contains("sql", StringComparison.OrdinalIgnoreCase))
            return MyEfVibeProvider.SqlServer;

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
                    yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateSettingsFileNames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in EnumerateSettingsFileNamesCore())
        {
            if (seen.Add(fileName))
                yield return fileName;
        }
    }

    private static IEnumerable<string> EnumerateSettingsFileNamesCore()
    {
        yield return "appsettings.json";
        yield return "appsettings.Development.json";

        var environmentName = ResolveEnvironmentName();

        if (!string.IsNullOrWhiteSpace(environmentName)
            && !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
            yield return $"appsettings.{environmentName}.json";
    }

    private static string? ResolveEnvironmentName()
    {
        foreach (var variableName in new[]
                 {
                     "ASPNETCORE_ENVIRONMENT",
                     "DOTNET_ENVIRONMENT",
                     "APPSETTING_ASPNETCORE_ENVIRONMENT",
                 })
        {
            var value = Environment.GetEnvironmentVariable(variableName);

            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
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
        var fromConnection = InferProviderFromConnectionString(connectionString);

        if (fromConnection.HasValue)
            return fromConnection;

        if (!Directory.Exists(outputDirectory))
            return null;

        return TryInferProviderFromDepsJson(outputDirectory, connectionString)
               ?? TryInferProviderFromOutputDlls(outputDirectory, connectionString);
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

        if (LooksLikeOracleConnection(connectionString))
            return MyEfVibeProvider.Oracle;

        if (LooksLikeSqlServerConnection(connectionString))
            return MyEfVibeProvider.SqlServer;

        return null;
    }

    internal static bool LooksLikeSqlServerConnection(string connectionString) =>
        connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("User ID=", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("Integrated Security=", StringComparison.OrdinalIgnoreCase)
        || (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            && connectionString.Contains(",1433", StringComparison.OrdinalIgnoreCase));

    internal static bool LooksLikeMySqlConnection(string connectionString) =>
        connectionString.Contains("Port=3306", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("Uid=", StringComparison.OrdinalIgnoreCase)
        || (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            && ContainsMySqlUserKey(connectionString));

    internal static bool LooksLikeOracleConnection(string connectionString) =>
        connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
        && (connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("User ID=", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsMySqlUserKey(string connectionString)
    {
        if (connectionString.Contains("User ID=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase))
            return false;

        return connectionString.Contains("User=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMariaDbConnection(string connectionString) =>
        connectionString.Contains("mariadb", StringComparison.OrdinalIgnoreCase);
}
