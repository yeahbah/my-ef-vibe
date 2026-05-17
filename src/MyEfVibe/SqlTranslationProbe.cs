namespace MyEfVibe;

internal static class SqlTranslationProbe
{
    private static readonly string[] TerminalMaterializationSuffixes =
    [
        ".ToListAsync()",
        ".ToArrayAsync()",
        ".CountAsync()",
        ".FirstAsync()",
        ".FirstOrDefaultAsync()",
        ".SingleAsync()",
        ".SingleOrDefaultAsync()",
        ".AnyAsync()",
        ".MaxAsync()",
        ".MinAsync()",
        ".AverageAsync()",
        ".SumAsync()",
        ".ToList()",
        ".ToArray()",
        ".Count()",
        ".First()",
        ".FirstOrDefault()",
        ".Single()",
        ".SingleOrDefault()",
        ".Any()",
        ".Max()",
        ".Min()",
        ".Average()",
        ".Sum()",
        ".AsEnumerable()",
    ];

    internal static string? TryCreateProbeExpression(string snippet)
    {
        var trimmed = snippet.Trim().TrimEnd(';').Trim();

        foreach (var suffix in TerminalMaterializationSuffixes)
        {
            if (!trimmed.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            var probe = trimmed[..^suffix.Length].TrimEnd();

            return string.IsNullOrWhiteSpace(probe) ? null : probe;
        }

        return null;
    }
}
