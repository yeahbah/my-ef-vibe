namespace MyEfVibe;

internal static class LinqQueryWarningRules
{
    internal static IReadOnlyList<(string RuleId, string Message)> AnalyzeSnippet(string snippet)
    {
        var warnings = new List<(string RuleId, string Message)>();
        var normalized = snippet.ReplaceLineEndings("\n");

        if (normalized.Contains("AsEnumerable(", StringComparison.Ordinal))
        {
            warnings.Add((
                "client-eval",
                "Uses AsEnumerable() — may force client-side evaluation."));
        }

        if (normalized.Contains(".ToList()", StringComparison.Ordinal)
            || normalized.Contains(".ToListAsync(", StringComparison.Ordinal)
            || normalized.Contains(".ToArray()", StringComparison.Ordinal)
            || normalized.Contains(".ToArrayAsync(", StringComparison.Ordinal))
        {
            if (!normalized.Contains(".Take(", StringComparison.Ordinal)
                && !normalized.Contains(".TakeAsync(", StringComparison.Ordinal))
            {
                warnings.Add((
                    "unbounded-materialize",
                    "Materializes results without Take() — may load a large result set."));
            }
        }

        var includeCount = CountOccurrences(normalized, ".Include(")
                         + CountOccurrences(normalized, ".ThenInclude(");

        if (includeCount >= 2)
        {
            warnings.Add((
                "cartesian",
                $"Multiple Include/ThenInclude calls ({includeCount}) — watch for cartesian explosion."));
        }

        if ((normalized.Contains(".Take(", StringComparison.Ordinal)
             || normalized.Contains(".TakeAsync(", StringComparison.Ordinal))
            && !normalized.Contains("OrderBy", StringComparison.Ordinal))
        {
            warnings.Add((
                "unordered-take",
                "Take() without OrderBy — row order is undefined."));
        }

        if (normalized.Contains("FromSqlRaw(", StringComparison.Ordinal)
            || normalized.Contains("ExecuteSqlRaw(", StringComparison.Ordinal))
        {
            warnings.Add((
                "raw-sql",
                "Uses raw SQL — verify parameterization and indexing."));
        }

        return warnings;
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
