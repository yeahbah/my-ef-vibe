using System.Text;

namespace MyEfVibe;

internal static class SqliteConnectionStringNormalizer
{
    private const string DataSourcePrefix = "Data Source=";

    internal static string Normalize(
        string connectionString,
        string startupProjectPath,
        string? efOutputDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString)
            || !LooksLikeSqliteConnection(connectionString))
        {
            return connectionString;
        }

        if (!TryParseDataSource(connectionString, out var dataSource, out var usesPrefix, out var trailingOptions))
        {
            return connectionString;
        }

        if (IsLiteralDataSource(dataSource) || IsInMemoryMode(trailingOptions))
        {
            return connectionString;
        }

        if (Path.IsPathRooted(dataSource))
        {
            return FormatDataSource(dataSource, usesPrefix, trailingOptions);
        }

        foreach (var searchRoot in EnumerateSearchRoots(startupProjectPath, efOutputDirectory))
        {
            var candidate = Path.GetFullPath(Path.Combine(searchRoot, dataSource));

            if (File.Exists(candidate))
            {
                return FormatDataSource(candidate, usesPrefix, trailingOptions);
            }
        }

        var fallbackRoot = Path.GetDirectoryName(startupProjectPath)!;
        var fallbackPath = Path.GetFullPath(Path.Combine(fallbackRoot, dataSource));

        return FormatDataSource(fallbackPath, usesPrefix, trailingOptions);
    }

    internal static bool LooksLikeSqliteConnection(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        // SQL Server and other server-based providers also use "Data Source=" — do not treat as SQLite.
        if (LooksLikeServerBasedConnection(connectionString))
        {
            return false;
        }

        if (connectionString.Contains(DataSourcePrefix, StringComparison.OrdinalIgnoreCase)
            && TryParseDataSource(connectionString, out var dataSource, out _, out _))
        {
            if (dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
                || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                || dataSource.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase)
               && !connectionString.Contains(';', StringComparison.Ordinal);
    }

    private static bool LooksLikeServerBasedConnection(string connectionString)
    {
        return connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase)
               || connectionString.Contains("User ID=", StringComparison.OrdinalIgnoreCase)
               || connectionString.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)
               || connectionString.Contains("Integrated Security=", StringComparison.OrdinalIgnoreCase)
               || connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
               || connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
               || (connectionString.Contains(DataSourcePrefix, StringComparison.OrdinalIgnoreCase)
                   && TryParseDataSource(connectionString, out var dataSource, out _, out _)
                   && dataSource.Contains(',', StringComparison.Ordinal));
    }

    private static bool TryParseDataSource(
        string connectionString,
        out string dataSource,
        out bool usesPrefix,
        out string trailingOptions)
    {
        dataSource = string.Empty;
        usesPrefix = false;
        trailingOptions = string.Empty;

        if (connectionString.StartsWith(DataSourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            usesPrefix = true;
            var remainder = connectionString[DataSourcePrefix.Length..].TrimStart();

            if (!TryReadConnectionStringValue(remainder, out dataSource, out var consumed))
            {
                return false;
            }

            var rest = remainder[consumed..].TrimStart();

            if (rest.StartsWith(';'))
            {
                trailingOptions = rest[1..].Trim();
            }

            return !string.IsNullOrWhiteSpace(dataSource);
        }

        if (connectionString.Contains(';', StringComparison.Ordinal))
        {
            return false;
        }

        dataSource = connectionString.Trim().Trim('"');

        return !string.IsNullOrWhiteSpace(dataSource);
    }

    private static bool TryReadConnectionStringValue(string remainder, out string value, out int consumed)
    {
        value = string.Empty;
        consumed = 0;

        if (remainder.Length == 0)
        {
            return false;
        }

        if (remainder[0] == '"')
        {
            var builder = new StringBuilder();
            var index = 1;

            while (index < remainder.Length)
            {
                if (remainder[index] == '"')
                {
                    if (index + 1 < remainder.Length && remainder[index + 1] == '"')
                    {
                        builder.Append('"');
                        index += 2;
                        continue;
                    }

                    value = builder.ToString();
                    consumed = index + 1;

                    return !string.IsNullOrWhiteSpace(value);
                }

                builder.Append(remainder[index]);
                index++;
            }

            return false;
        }

        var separatorIndex = remainder.IndexOf(';');

        if (separatorIndex < 0)
        {
            value = remainder.Trim().Trim('"');
            consumed = remainder.Length;

            return !string.IsNullOrWhiteSpace(value);
        }

        value = remainder[..separatorIndex].Trim().Trim('"');
        consumed = separatorIndex;

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsInMemoryMode(string trailingOptions)
    {
        return TryGetConnectionOption(trailingOptions, "Mode", out var mode)
               && mode.Equals("Memory", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetConnectionOption(string options, string key, out string value)
    {
        value = string.Empty;
        var index = 0;

        while (index < options.Length)
        {
            while (index < options.Length && (options[index] == ';' || char.IsWhiteSpace(options[index])))
            {
                index++;
            }

            if (index >= options.Length)
            {
                break;
            }

            var keyStart = index;

            while (index < options.Length && options[index] != '=')
            {
                index++;
            }

            if (index >= options.Length)
            {
                break;
            }

            var optionKey = options[keyStart..index].Trim();
            index++;

            if (!TryReadConnectionStringValue(options[index..], out var optionValue, out var consumed))
            {
                break;
            }

            if (optionKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = optionValue;

                return true;
            }

            index += consumed;
        }

        return false;
    }

    private static bool IsLiteralDataSource(string dataSource)
    {
        return dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
               || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
               || dataSource.Contains("mode=memory", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDataSource(
        string absoluteOrRootedPath,
        bool usesPrefix,
        string trailingOptions)
    {
        var formatted = usesPrefix
            ? $"{DataSourcePrefix}{QuoteDataSourceIfNeeded(absoluteOrRootedPath)}"
            : absoluteOrRootedPath;

        if (!usesPrefix || string.IsNullOrEmpty(trailingOptions))
        {
            return formatted;
        }

        return $"{formatted};{trailingOptions}";
    }

    private static string QuoteDataSourceIfNeeded(string path)
    {
        if (!path.Contains(';')
            && !path.Contains('"')
            && !path.StartsWith(' ')
            && !path.EndsWith(' '))
        {
            return path;
        }

        return $"\"{path.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static IEnumerable<string> EnumerateSearchRoots(
        string startupProjectPath,
        string? efOutputDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in EnumerateDirectoryChain(Path.GetDirectoryName(startupProjectPath)))
        {
            if (seen.Add(root))
            {
                yield return root;
            }
        }

        if (!string.IsNullOrWhiteSpace(efOutputDirectory))
        {
            foreach (var root in EnumerateDirectoryChain(efOutputDirectory))
            {
                if (seen.Add(root))
                {
                    yield return root;
                }
            }
        }

        foreach (var root in EnumerateDirectoryChain(Directory.GetCurrentDirectory()))
        {
            if (seen.Add(root))
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoryChain(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            yield break;
        }

        var current = new DirectoryInfo(startDirectory);

        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}