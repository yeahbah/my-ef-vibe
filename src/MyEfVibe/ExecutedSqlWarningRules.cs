namespace MyEfVibe;

internal static class ExecutedSqlWarningRules
{
    internal static void AddExecutedSqlWarnings(
        string snippet,
        IReadOnlyList<string> executedSql,
        ICollection<string> warnings)
    {
        if (executedSql.Count == 0 || !SnippetUsesTerminalFirst(snippet))
        {
            return;
        }

        foreach (var sql in executedSql)
        {
            if (!LooksLikeSelectStatement(sql) || SqlContainsRowLimit(sql))
            {
                continue;
            }

            warnings.Add(
                "Executed SQL for First()/FirstOrDefault() has no LIMIT/TOP/FETCH — PostgreSQL may scan the full table. "
                + "Prefer .Where(...).Take(1).FirstOrDefaultAsync(), ensure `using Microsoft.EntityFrameworkCore` for async "
                + "(not System.Linq.Async), and avoid AsEnumerable() before First.");

            return;
        }
    }

    private static bool SnippetUsesTerminalFirst(string snippet)
    {
        var normalized = snippet.ReplaceLineEndings("\n");

        foreach (var suffix in new[]
                 {
                     ".First()",
                     ".FirstOrDefault()",
                     ".FirstAsync(",
                     ".FirstOrDefaultAsync("
                 })
        {
            if (normalized.Contains(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeSelectStatement(string sql)
    {
        var trimmed = sql.TrimStart();

        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
               || (trimmed.StartsWith("--", StringComparison.Ordinal)
                   && trimmed.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
    }

    private static bool SqlContainsRowLimit(string sql)
    {
        if (sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (sql.Contains("FETCH FIRST", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("FETCH NEXT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return sql.Contains("TOP (", StringComparison.OrdinalIgnoreCase)
               || sql.Contains("TOP(", StringComparison.OrdinalIgnoreCase);
    }
}