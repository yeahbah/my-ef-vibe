namespace MyEfVibe;

internal static class SnippetWarningsAnalyzer
{
    internal static IReadOnlyList<string> Analyze(string snippet)
    {
        var warnings = new List<string>();
        var normalized = snippet.ReplaceLineEndings("\n");

        if (normalized.Contains("AsEnumerable(", StringComparison.Ordinal))
            warnings.Add("Uses AsEnumerable() — may force client-side evaluation.");

        if (normalized.Contains(".ToList()", StringComparison.Ordinal)
            && !normalized.Contains(".Take(", StringComparison.Ordinal))
            warnings.Add("ToList() without Take() may materialize a large result set.");

        var includeCount = CountOccurrences(normalized, ".Include(");

        if (includeCount >= 2)
            warnings.Add($"Multiple Include() calls ({includeCount}) — watch for cartesian explosion.");

        if (normalized.Contains(".Take(", StringComparison.Ordinal)
            && !normalized.Contains("OrderBy", StringComparison.Ordinal))
            warnings.Add("Take() without OrderBy — row order is undefined.");

        if (normalized.Contains("TagWith(", StringComparison.Ordinal))
            warnings.Add("Query uses TagWith() — look for the tag in SQL output.");

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
