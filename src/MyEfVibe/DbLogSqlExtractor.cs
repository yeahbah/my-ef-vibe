using System.Text.RegularExpressions;

namespace MyEfVibe;

/// <summary>
/// Pulls executable SQL out of captured database log text (sql-only or verbose EF diagnostic format).
/// </summary>
internal static partial class DbLogSqlExtractor
{
    internal static string? ExtractExecutableSql(string? captured)
    {
        if (string.IsNullOrWhiteSpace(captured))
            return null;

        var sqlLines = new List<string>();
        var startedSql = false;

        foreach (var line in captured.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith("-- duration:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (trimmed.StartsWith("-- parameters:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (VerboseEventHeaderRegex().IsMatch(trimmed))
                continue;

            if (trimmed.StartsWith("-- @", StringComparison.Ordinal))
                continue;

            if (LooksLikeSqlLine(trimmed) || startedSql)
            {
                startedSql = true;
                sqlLines.Add(line);
            }
        }

        var body = string.Join(Environment.NewLine, sqlLines).Trim();

        return ContainsSqlKeyword(body) ? body : null;
    }

    internal static string? SelectPlanSql(IReadOnlyList<string> executedSql, string? translatedSql)
    {
        for (var index = executedSql.Count - 1; index >= 0; index--)
        {
            var entry = executedSql[index];

            if (!entry.Contains("CommandExecuted", StringComparison.OrdinalIgnoreCase)
                && !ContainsSqlKeyword(entry))
                continue;

            return entry;
        }

        foreach (var entry in executedSql)
        {
            if (ContainsSqlKeyword(entry))
                return entry;
        }

        return translatedSql;
    }

    private static bool LooksLikeSqlLine(string trimmed) =>
        ContainsSqlKeyword(trimmed)
        || trimmed.StartsWith("SET ", StringComparison.OrdinalIgnoreCase)
        || trimmed.StartsWith("DECLARE ", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsSqlKeyword(string message)
    {
        foreach (var keyword in new[] { "SELECT ", "INSERT ", "UPDATE ", "DELETE ", "FROM ", "WITH " })
        {
            if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    [GeneratedRegex(@"^\[[^\]]+\]\s+\S")]
    private static partial Regex VerboseEventHeaderRegex();
}
