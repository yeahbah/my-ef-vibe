using MyEfVibe.Linq;

namespace MyEfVibe;

/// <summary>
///     Applies server-side paging to LINQ snippets by inserting <c>Skip</c>/<c>Take</c> before list materializers.
/// </summary>
internal static class QueryPagingRewriter
{
    internal const int DefaultPageSize = SqlTranslationProbe.DefaultMaterializationTake;

    private static readonly string[] ListMaterializationMethodNames =
    [
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync"
    ];

    internal static bool SupportsPaging(string snippet)
    {
        return TryApplyPaging(snippet, 0, DefaultPageSize) is not null;
    }

    internal static string? TryApplyPaging(string snippet, int skip, int pageSize)
    {
        if (pageSize <= 0 || skip < 0)
        {
            return null;
        }

        var trimmed = snippet.Trim().TrimEnd(';').Trim();

        if (string.IsNullOrWhiteSpace(trimmed)
            || !LinqEfQueryHeuristics.LooksLikeEfQuery(trimmed))
        {
            return null;
        }

        foreach (var methodName in ListMaterializationMethodNames)
        {
            var needle = $".{methodName}(";
            var index = trimmed.LastIndexOf(needle, StringComparison.Ordinal);

            if (index < 0)
            {
                continue;
            }

            var openParenIndex = index + needle.Length - 1;

            if (!SqlTranslationProbe.TryFindClosingParenthesis(trimmed, openParenIndex, out var closeParenIndex)
                || !SqlTranslationProbe.IsEndOfExpression(trimmed, closeParenIndex + 1))
            {
                continue;
            }

            var queryable = StripTrailingSkipTake(trimmed[..index].TrimEnd());
            var suffix = trimmed[index..];

            if (string.IsNullOrWhiteSpace(queryable))
            {
                return null;
            }

            var paged = skip == 0
                ? $"{queryable}.Take({pageSize})"
                : $"{queryable}.Skip({skip}).Take({pageSize})";

            return paged + suffix;
        }

        return null;
    }

    private static string StripTrailingSkipTake(string queryable)
    {
        var working = queryable;

        while (true)
        {
            if (TryParseTrailingCall(working, "Take", out _, out var beforeTake))
            {
                working = beforeTake;
                continue;
            }

            if (TryParseTrailingCall(working, "Skip", out _, out var beforeSkip))
            {
                working = beforeSkip;
                continue;
            }

            return working;
        }
    }

    private static bool TryParseTrailingCall(
        string snippet,
        string methodName,
        out string argument,
        out string source)
    {
        argument = string.Empty;
        source = string.Empty;

        var suffix = $".{methodName}(";
        var callIndex = snippet.LastIndexOf(suffix, StringComparison.Ordinal);

        if (callIndex < 0)
        {
            return false;
        }

        var openParenIndex = callIndex + suffix.Length - 1;

        if (!SqlTranslationProbe.TryExtractParenthesizedContent(snippet, openParenIndex, out argument)
            || !SqlTranslationProbe.TryFindClosingParenthesis(snippet, openParenIndex, out var closeParenIndex)
            || !SqlTranslationProbe.IsEndOfExpression(snippet, closeParenIndex + 1))
        {
            return false;
        }

        source = snippet[..callIndex].TrimEnd();

        return !string.IsNullOrWhiteSpace(source);
    }
}
