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
        var trimmed = sql.TrimStart();

        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var match = FirstKeywordRegex().Match(trimmed);

        return match.Success
            ? match.Groups["keyword"].Value.ToUpperInvariant()
            : string.Empty;
    }

    [GeneratedRegex(@"^(?<keyword>[A-Za-z_]+)", RegexOptions.CultureInvariant)]
    private static partial Regex FirstKeywordRegex();
}
