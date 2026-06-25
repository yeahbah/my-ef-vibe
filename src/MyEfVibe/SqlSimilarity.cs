namespace MyEfVibe;

internal static class SqlSimilarity
{
    internal static double Compare(string? originalSql, string? translatedSql)
    {
        if (string.IsNullOrWhiteSpace(originalSql) || string.IsNullOrWhiteSpace(translatedSql))
        {
            return 0;
        }

        var left = Tokenize(originalSql);
        var right = Tokenize(translatedSql);

        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var intersection = left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count();
        var union = left.Union(right, StringComparer.OrdinalIgnoreCase).Count();

        return union == 0 ? 0 : Math.Round(intersection / (double)union, 2);
    }

    private static HashSet<string> Tokenize(string sql)
    {
        return sql
            .Replace("(", " ", StringComparison.Ordinal)
            .Replace(")", " ", StringComparison.Ordinal)
            .Replace(",", " ", StringComparison.Ordinal)
            .Replace(";", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
