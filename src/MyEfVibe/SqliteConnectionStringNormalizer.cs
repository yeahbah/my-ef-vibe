namespace MyEfVibe;

internal static class SqliteConnectionStringNormalizer
{
    private const string DataSourcePrefix = "Data Source=";

    internal static string Normalize(
        string connectionString,
        string startupProjectPath,
        string? efOutputDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        if (!TryParseDataSource(connectionString, out var dataSource, out var usesPrefix))
            return connectionString;

        if (IsLiteralDataSource(dataSource))
            return connectionString;

        if (Path.IsPathRooted(dataSource))
            return FormatDataSource(dataSource, usesPrefix);

        foreach (var searchRoot in EnumerateSearchRoots(startupProjectPath, efOutputDirectory))
        {
            var candidate = Path.GetFullPath(Path.Combine(searchRoot, dataSource));

            if (File.Exists(candidate))
                return FormatDataSource(candidate, usesPrefix);
        }

        var fallbackRoot = Path.GetDirectoryName(startupProjectPath)!;
        var fallbackPath = Path.GetFullPath(Path.Combine(fallbackRoot, dataSource));

        return FormatDataSource(fallbackPath, usesPrefix);
    }

    internal static bool LooksLikeSqliteConnection(string connectionString) =>
        connectionString.Contains(DataSourcePrefix, StringComparison.OrdinalIgnoreCase)
        || (connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase));

    private static bool TryParseDataSource(
        string connectionString,
        out string dataSource,
        out bool usesPrefix)
    {
        dataSource = string.Empty;
        usesPrefix = false;

        if (connectionString.StartsWith(DataSourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            usesPrefix = true;
            dataSource = connectionString[DataSourcePrefix.Length..].Trim().Trim('"');
            SplitAdditionalOptions(ref dataSource);

            return !string.IsNullOrWhiteSpace(dataSource);
        }

        if (connectionString.Contains(';', StringComparison.Ordinal))
            return false;

        dataSource = connectionString.Trim().Trim('"');

        return !string.IsNullOrWhiteSpace(dataSource);
    }

    private static void SplitAdditionalOptions(ref string dataSource)
    {
        var separatorIndex = dataSource.IndexOf(';', StringComparison.Ordinal);

        if (separatorIndex < 0)
            return;

        dataSource = dataSource[..separatorIndex].Trim();
    }

    private static bool IsLiteralDataSource(string dataSource) =>
        dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
        || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
        || dataSource.Contains("mode=memory", StringComparison.OrdinalIgnoreCase);

    private static string FormatDataSource(string absoluteOrRootedPath, bool usesPrefix) =>
        usesPrefix
            ? $"{DataSourcePrefix}{absoluteOrRootedPath}"
            : absoluteOrRootedPath;

    private static IEnumerable<string> EnumerateSearchRoots(
        string startupProjectPath,
        string? efOutputDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in EnumerateDirectoryChain(Path.GetDirectoryName(startupProjectPath)))
        {
            if (seen.Add(root))
                yield return root;
        }

        if (!string.IsNullOrWhiteSpace(efOutputDirectory))
        {
            foreach (var root in EnumerateDirectoryChain(efOutputDirectory))
            {
                if (seen.Add(root))
                    yield return root;
            }
        }

        foreach (var root in EnumerateDirectoryChain(Directory.GetCurrentDirectory()))
        {
            if (seen.Add(root))
                yield return root;
        }
    }

    private static IEnumerable<string> EnumerateDirectoryChain(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            yield break;

        var current = new DirectoryInfo(startDirectory);

        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}
