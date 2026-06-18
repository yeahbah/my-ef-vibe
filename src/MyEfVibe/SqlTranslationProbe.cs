using MyEfVibe.Linq;

namespace MyEfVibe;

internal static class SqlTranslationProbe
{
    private static readonly string[] AggregateTerminalMethods =
    [
        "Count",
        "CountAsync",
        "Any",
        "AnyAsync",
        "Max",
        "MaxAsync",
        "Min",
        "MinAsync",
        "Sum",
        "SumAsync",
        "Average",
        "AverageAsync"
    ];

    /// <summary>
    ///     Terminal operators removed before <c>ToQueryString()</c> for materializers. Aggregate terminals
    ///     (<c>Count</c>, <c>Any</c>, etc.) stay on the probe so deep scan can capture translated SQL.
    ///     For <c>First</c>/<c>Single</c>, the probe keeps an equivalent <c>Take(n)</c> (and <c>Where</c> when needed).
    /// </summary>
    private static readonly (string Suffix, int? TakeLimit)[] TerminalMaterializationSuffixes =
    [
        (".ToListAsync()", null),
        (".ToArrayAsync()", null),
        (".CountAsync()", null),
        (".FirstOrDefaultAsync()", 1),
        (".FirstAsync()", 1),
        (".SingleOrDefaultAsync()", 2),
        (".SingleAsync()", 2),
        (".AnyAsync()", null),
        (".MaxAsync()", null),
        (".MinAsync()", null),
        (".AverageAsync()", null),
        (".SumAsync()", null),
        (".ToDictionaryAsync()", null),
        (".ToDictionary()", null),
        (".ToList()", null),
        (".ToArray()", null),
        (".Count()", null),
        (".FirstOrDefault()", 1),
        (".First()", 1),
        (".SingleOrDefault()", 2),
        (".Single()", 2),
        (".Any()", null),
        (".Max()", null),
        (".Min()", null),
        (".Average()", null),
        (".Sum()", null),
        (".AsEnumerable()", null)
    ];

    private static readonly string[] TerminalMethodNames =
    [
        "ToListAsync",
        "ToArrayAsync",
        "CountAsync",
        "FirstOrDefaultAsync",
        "FirstAsync",
        "SingleOrDefaultAsync",
        "SingleAsync",
        "AnyAsync",
        "MaxAsync",
        "MinAsync",
        "AverageAsync",
        "SumAsync",
        "ToDictionaryAsync",
        "ToDictionary",
        "ToList",
        "ToArray",
        "Count",
        "FirstOrDefault",
        "First",
        "SingleOrDefault",
        "Single",
        "Any",
        "Max",
        "Min",
        "Average",
        "Sum",
        "AsEnumerable"
    ];

    /// <summary>
    ///     Rewrites terminal <c>First</c>/<c>Single</c> operators on EF queries to include <c>Take(n)</c> before execution
    ///     so providers emit <c>LIMIT</c>/<c>TOP</c> instead of materializing the full result set client-side.
    /// </summary>
    internal static string? TryRewriteBoundedTerminalQuery(string snippet)
    {
        var trimmed = snippet.Trim().TrimEnd(';').Trim();

        if (string.IsNullOrWhiteSpace(trimmed)
            || !LinqEfQueryHeuristics.LooksLikeEfQuery(trimmed)
            || trimmed.Contains(".Take(", StringComparison.Ordinal)
            || trimmed.Contains(".TakeAsync(", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var (suffix, takeLimit) in TerminalMaterializationSuffixes)
        {
            if (takeLimit is null || !trimmed.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var queryable = trimmed[..^suffix.Length].TrimEnd();

            if (string.IsNullOrWhiteSpace(queryable))
            {
                return null;
            }

            var bounded = TryFinalizeProbe(queryable, takeLimit);

            var remappedSuffix = RemapTerminalSuffixForExecution(suffix);

            return bounded is null ? null : bounded + remappedSuffix;
        }

        var terminalRewrite = TryRewriteBoundedTrailingTerminalCall(trimmed);

        return terminalRewrite;
    }

    private static string? TryRewriteBoundedTrailingTerminalCall(string expression)
    {
        foreach (var methodName in TerminalMethodNames)
        {
            var takeLimit = TryGetTakeLimitForMethod(methodName);

            if (takeLimit is null)
            {
                continue;
            }

            var needle = $".{methodName}(";
            var index = expression.LastIndexOf(needle, StringComparison.Ordinal);

            if (index < 0)
            {
                continue;
            }

            var openParenIndex = index + needle.Length - 1;

            if (!TryFindClosingParenthesis(expression, openParenIndex, out var closeParenIndex))
            {
                continue;
            }

            if (!IsEndOfExpression(expression, closeParenIndex + 1))
            {
                continue;
            }

            var queryable = expression[..index].TrimEnd();
            var suffix = expression[index..];

            if (string.IsNullOrWhiteSpace(queryable) || !LinqEfQueryHeuristics.LooksLikeEfQuery(expression))
            {
                continue;
            }

            if (!TryExtractParenthesizedContent(expression, openParenIndex, out var arguments))
            {
                continue;
            }

            var bounded = TryFinalizeProbe(queryable, takeLimit, arguments);

            var remappedSuffix = RemapTerminalSuffixForExecution(suffix);

            return bounded is null ? null : bounded + remappedSuffix;
        }

        return null;
    }

    private static string RemapTerminalSuffixForExecution(string suffix)
    {
        return suffix switch
        {
            ".First()" => ".FirstOrDefault()",
            ".FirstAsync()" => ".FirstOrDefaultAsync()",
            _ => suffix
        };
    }

    internal static bool ContainsEagerLoad(string expression)
    {
        return expression.Contains(".Include(", StringComparison.Ordinal)
               || expression.Contains(".ThenInclude(", StringComparison.Ordinal);
    }

    internal static bool LooksLikeAggregateTerminalProbe(string probe)
    {
        var trimmed = probe.Trim().TrimEnd(';').Trim();

        foreach (var methodName in AggregateTerminalMethods)
        {
            var needle = $".{methodName}(";
            var index = trimmed.LastIndexOf(needle, StringComparison.Ordinal);

            if (index < 0)
            {
                continue;
            }

            var openParenIndex = index + needle.Length - 1;

            if (!TryFindClosingParenthesis(trimmed, openParenIndex, out var closeParenIndex))
            {
                continue;
            }

            if (!IsEndOfExpression(trimmed, closeParenIndex + 1))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    internal static string? TryCreateProbeExpression(string snippet)
    {
        var trimmed = snippet.Trim().TrimEnd(';').Trim();

        foreach (var (suffix, takeLimit) in TerminalMaterializationSuffixes)
        {
            if (!trimmed.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var queryable = trimmed[..^suffix.Length].TrimEnd();

            if (TryGetAggregateMethodFromSuffix(suffix, out var aggregateMethod))
            {
                return TryFinalizeAggregateProbe(queryable, aggregateMethod);
            }

            return TryFinalizeProbe(queryable, takeLimit);
        }

        var terminalProbe = TryStripTrailingTerminalCall(trimmed);

        if (terminalProbe is not null)
        {
            return terminalProbe;
        }

        return TryAsBareQueryableProbe(trimmed);
    }

    /// <summary>
    ///     Accepts deferred query expressions (e.g. assigned to a local) that have no terminal operator.
    /// </summary>
    private static string? TryAsBareQueryableProbe(string expression)
    {
        var probe = expression.Trim().TrimEnd(';').Trim();

        foreach (var suffix in new[] { ".AsQueryable()", ".AsQueryable();" })
        {
            if (probe.EndsWith(suffix, StringComparison.Ordinal))
            {
                probe = probe[..^suffix.Length].TrimEnd();
                break;
            }
        }

        if (LooksLikeCompositeAnonymousExpression(probe))
        {
            return null;
        }

        return LinqEfQueryHeuristics.LooksLikeEfQuery(probe) ? probe : null;
    }

    private static bool LooksLikeCompositeAnonymousExpression(string expression)
    {
        var trimmed = expression.TrimStart();

        if (trimmed.StartsWith("new[]", StringComparison.Ordinal))
        {
            return false;
        }

        if (!trimmed.StartsWith("new", StringComparison.Ordinal))
        {
            return false;
        }

        var index = 3;

        while (index < trimmed.Length && char.IsWhiteSpace(trimmed[index]))
        {
            index++;
        }

        return index < trimmed.Length && trimmed[index] == '{';
    }

    internal static bool TryExtractParenthesizedContent(string text, int openParenIndex, out string content)
    {
        content = string.Empty;

        if (!TryFindClosingParenthesis(text, openParenIndex, out var closeParenIndex))
        {
            return false;
        }

        content = text[(openParenIndex + 1)..closeParenIndex];

        return true;
    }

    private static string? TryStripTrailingTerminalCall(string expression)
    {
        foreach (var methodName in TerminalMethodNames)
        {
            var needle = $".{methodName}(";
            var index = expression.LastIndexOf(needle, StringComparison.Ordinal);

            if (index < 0)
            {
                continue;
            }

            var openParenIndex = index + needle.Length - 1;

            if (!TryFindClosingParenthesis(expression, openParenIndex, out var closeParenIndex))
            {
                continue;
            }

            if (!IsEndOfExpression(expression, closeParenIndex + 1))
            {
                continue;
            }

            var queryable = expression[..index].TrimEnd();

            if (string.IsNullOrWhiteSpace(queryable))
            {
                return null;
            }

            if (!TryExtractParenthesizedContent(expression, openParenIndex, out var arguments))
            {
                return null;
            }

            if (IsAggregateTerminalMethod(methodName))
            {
                return TryFinalizeAggregateProbe(queryable, methodName, arguments);
            }

            var takeLimit = TryGetTakeLimitForMethod(methodName);

            return TryFinalizeProbe(queryable, takeLimit, arguments);
        }

        return null;
    }

    private static bool IsAggregateTerminalMethod(string methodName)
    {
        return AggregateTerminalMethods.Contains(methodName, StringComparer.Ordinal);
    }

    private static bool TryGetAggregateMethodFromSuffix(string suffix, out string methodName)
    {
        methodName = string.Empty;

        if (!suffix.StartsWith(".", StringComparison.Ordinal)
            || !suffix.EndsWith("()", StringComparison.Ordinal))
        {
            return false;
        }

        methodName = suffix[1..^2];

        return IsAggregateTerminalMethod(methodName);
    }

    private static string? TryFinalizeAggregateProbe(
        string queryable,
        string methodName,
        string? terminalArguments = null)
    {
        if (string.IsNullOrWhiteSpace(queryable))
        {
            return null;
        }

        var syncMethod = RemapAsyncAggregateMethod(methodName);
        var predicate = TryExtractPredicateArgument(terminalArguments);

        if (!string.IsNullOrWhiteSpace(predicate))
        {
            return $"{queryable}.{syncMethod}({predicate})";
        }

        return $"{queryable}.{syncMethod}()";
    }

    private static string RemapAsyncAggregateMethod(string methodName)
    {
        return methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName[..^5]
            : methodName;
    }

    private static string? TryFinalizeProbe(string queryable, int? takeLimit, string? terminalArguments = null)
    {
        if (string.IsNullOrWhiteSpace(queryable))
        {
            return null;
        }

        if (takeLimit is null || ContainsEagerLoad(queryable))
        {
            return queryable;
        }

        var predicate = TryExtractPredicateArgument(terminalArguments);

        if (!string.IsNullOrWhiteSpace(predicate))
        {
            return $"{queryable}.Where({predicate}).Take({takeLimit.Value})";
        }

        return $"{queryable}.Take({takeLimit.Value})";
    }

    internal static string? TryExtractPredicateArgument(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        foreach (var part in SplitTopLevelCommaSeparated(arguments))
        {
            var trimmed = part.Trim();

            if (LooksLikePredicate(trimmed))
            {
                return trimmed;
            }
        }

        return LooksLikePredicate(arguments) ? arguments.Trim() : null;
    }

    private static bool LooksLikePredicate(string arguments)
    {
        return arguments.Contains("=>", StringComparison.Ordinal);
    }

    private static IEnumerable<string> SplitTopLevelCommaSeparated(string arguments)
    {
        var start = 0;
        var depth = 0;

        for (var index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case '(':
                case '[':
                case '{':
                    depth++;
                    break;

                case ')':
                case ']':
                case '}':
                    depth--;
                    break;

                case ',' when depth == 0:
                    yield return arguments[start..index];
                    start = index + 1;
                    break;

                case '"':
                    index = SkipStringLiteral(arguments, index);
                    break;

                case '\'':
                    index = SkipCharLiteral(arguments, index);
                    break;
            }
        }

        if (start < arguments.Length)
        {
            yield return arguments[start..];
        }
    }

    private static int? TryGetTakeLimitForMethod(string methodName)
    {
        return methodName switch
        {
            "First" or "FirstAsync" or "FirstOrDefault" or "FirstOrDefaultAsync" => 1,
            "Single" or "SingleAsync" or "SingleOrDefault" or "SingleOrDefaultAsync" => 2,
            _ => null
        };
    }

    internal static bool IsEndOfExpression(string expression, int startIndex)
    {
        for (var index = startIndex; index < expression.Length; index++)
        {
            var character = expression[index];

            if (character is ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    internal static bool TryFindClosingParenthesis(string text, int openParenIndex, out int closeParenIndex)
    {
        closeParenIndex = -1;

        if (openParenIndex < 0 || openParenIndex >= text.Length || text[openParenIndex] != '(')
        {
            return false;
        }

        var depth = 0;

        for (var index = openParenIndex; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '(':
                    depth++;
                    break;

                case ')':
                    depth--;

                    if (depth == 0)
                    {
                        closeParenIndex = index;
                        return true;
                    }

                    break;

                case '"':
                    index = SkipStringLiteral(text, index);

                    break;

                case '\'':
                    index = SkipCharLiteral(text, index);

                    break;
            }
        }

        return false;
    }

    private static int SkipStringLiteral(string text, int startIndex)
    {
        var index = startIndex + 1;

        while (index < text.Length)
        {
            if (text[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (text[index] == '"')
            {
                return index;
            }

            index++;
        }

        return text.Length - 1;
    }

    private static int SkipCharLiteral(string text, int startIndex)
    {
        var index = startIndex + 1;

        while (index < text.Length)
        {
            if (text[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (text[index] == '\'')
            {
                return index;
            }

            index++;
        }

        return text.Length - 1;
    }
}