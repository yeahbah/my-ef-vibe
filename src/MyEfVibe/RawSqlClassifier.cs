using System.Text.RegularExpressions;

namespace MyEfVibe;

internal static partial class RawSqlClassifier
{
    internal static bool LooksLikeQuery(string sql)
    {
        return FirstKeyword(sql) switch
        {
            "SELECT" or "WITH" or "SHOW" or "EXPLAIN" or "DESCRIBE" or "DESC" or "PRAGMA" or "TABLE" => true,
            _ => false
        };
    }

    private static string FirstKeyword(string sql)
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

        if (remaining.Length == 0)
        {
            return string.Empty;
        }

        var match = FirstKeywordRegex().Match(remaining);

        return match.Success
            ? match.Groups["keyword"].Value.ToUpperInvariant()
            : string.Empty;
    }

    [GeneratedRegex(@"^(?<keyword>[A-Za-z_]+)", RegexOptions.CultureInvariant)]
    private static partial Regex FirstKeywordRegex();
}
