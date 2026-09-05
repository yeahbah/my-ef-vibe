using System.Text.RegularExpressions;

namespace MyEfVibe;

internal static partial class RawSqlClassifier
{
    private static readonly HashSet<string> QueryKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT",
        "WITH",
        "SHOW",
        "EXPLAIN",
        "DESCRIBE",
        "DESC",
        "PRAGMA",
        "TABLE"
    };

    private static readonly HashSet<string> PreambleKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DECLARE",
        "SET"
    };

    internal static bool LooksLikeQuery(string sql)
    {
        foreach (var statement in EnumerateStatements(sql))
        {
            var keyword = FirstStatementKeyword(statement);

            if (string.IsNullOrEmpty(keyword))
            {
                continue;
            }

            if (PreambleKeywords.Contains(keyword))
            {
                continue;
            }

            if (QueryKeywords.Contains(keyword))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ContainsQueryStatement(string sql)
    {
        foreach (var statement in EnumerateStatements(sql))
        {
            var keyword = FirstStatementKeyword(statement);

            if (QueryKeywords.Contains(keyword))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Returns a single statement that is safe to wrap with EXPLAIN. Multi-statement batches
    ///     keep only the last query (or the first non-preamble statement) so trailing DML is never
    ///     executed when the provider prefixes EXPLAIN on the first statement only.
    /// </summary>
    internal static string? SelectExplainableStatement(string sql)
    {
        string? firstNonPreamble = null;
        string? lastQuery = null;

        foreach (var statement in EnumerateStatements(sql))
        {
            var keyword = FirstStatementKeyword(statement);

            if (string.IsNullOrEmpty(keyword) || PreambleKeywords.Contains(keyword))
            {
                continue;
            }

            firstNonPreamble ??= statement;

            if (QueryKeywords.Contains(keyword))
            {
                lastQuery = statement;
            }
        }

        return lastQuery ?? firstNonPreamble;
    }

    private static IEnumerable<string> EnumerateStatements(string sql)
    {
        foreach (var part in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                yield return part;
            }
        }
    }

    private static string FirstStatementKeyword(string statement)
    {
        var remaining = SkipLeadingComments(statement.TrimStart());

        if (remaining.Length == 0)
        {
            return string.Empty;
        }

        var match = FirstKeywordRegex().Match(remaining);

        return match.Success
            ? match.Groups["keyword"].Value.ToUpperInvariant()
            : string.Empty;
    }

    private static string SkipLeadingComments(string sql)
    {
        var remaining = sql.TrimStart();

        while (remaining.Length > 0)
        {
            if (remaining.StartsWith("--", StringComparison.Ordinal))
            {
                var newline = remaining.IndexOf('\n');
                if (newline < 0)
                {
                    return string.Empty;
                }

                remaining = remaining[(newline + 1)..].TrimStart();
                continue;
            }

            if (remaining.StartsWith("/*", StringComparison.Ordinal))
            {
                var end = remaining.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0)
                {
                    return string.Empty;
                }

                remaining = remaining[(end + 2)..].TrimStart();
                continue;
            }

            break;
        }

        return remaining;
    }

    [GeneratedRegex(@"^(?<keyword>[A-Za-z_]+)", RegexOptions.CultureInvariant)]
    private static partial Regex FirstKeywordRegex();
}
