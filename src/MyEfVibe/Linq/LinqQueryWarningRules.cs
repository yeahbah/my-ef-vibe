namespace MyEfVibe.Linq;

internal static class LinqQueryWarningRules
{
    internal static IReadOnlyList<(string RuleId, string Message)> AnalyzeSnippet(
        string snippet,
        string? containingMethodName = null)
    {
        var warnings = new List<(string RuleId, string Message)>();
        var normalized = snippet.ReplaceLineEndings("\n");

        if (normalized.Contains("AsEnumerable(", StringComparison.Ordinal))
        {
            warnings.Add((
                "client-eval",
                "Uses AsEnumerable() — may force client-side evaluation."));
        }

        if (MaterializesWithoutTake(normalized) && !IsIntentionalFullListMaterialization(containingMethodName))
        {
            warnings.Add((
                "unbounded-materialize",
                "Materializes results without Take() — may load a large result set."));
        }

        var topLevelIncludeCount = CountOccurrences(normalized, ".Include(");

        if (topLevelIncludeCount >= 2)
        {
            warnings.Add((
                "cartesian",
                $"Multiple Include calls ({topLevelIncludeCount}) — watch for cartesian explosion."));
        }

        if (HasUnorderedTakeWithoutRowCap(normalized))
        {
            warnings.Add((
                "unordered-take",
                "Take() without OrderBy — row order is undefined."));
        }

        if (UsesUnboundedTerminalFirst(normalized))
        {
            warnings.Add((
                "first-without-take",
                "First()/FirstOrDefault() without Take(1) — on PostgreSQL, executed SQL may omit LIMIT and scan the full table."));
        }

        AnalyzeRawSqlWarnings(normalized, warnings);

        return warnings;
    }

    private static void AnalyzeRawSqlWarnings(
        string normalized,
        List<(string RuleId, string Message)> warnings)
    {
        var hasParameterizedRaw = false;
        var hasUnparameterizedRaw = false;

        foreach (var method in new[] { "FromSqlRaw", "ExecuteSqlRaw", "FromSqlRawAsync", "ExecuteSqlRawAsync" })
        {
            foreach (var arguments in EnumerateRawSqlInvocationArguments(normalized, method))
            {
                if (HasSqlParameterArguments(arguments))
                {
                    hasParameterizedRaw = true;
                }
                else
                {
                    hasUnparameterizedRaw = true;
                }
            }
        }

        if (hasUnparameterizedRaw)
        {
            warnings.Add((
                "raw-sql-unparameterized",
                "Uses raw SQL without separate SQL parameters — injection and plan-cache risk."));
        }
        else if (hasParameterizedRaw)
        {
            warnings.Add((
                "raw-sql",
                "Uses parameterized raw SQL — verify indexing and execution plan."));
        }
    }

    private static IEnumerable<string> EnumerateRawSqlInvocationArguments(string text, string methodName)
    {
        var needle = $"{methodName}(";

        for (var index = 0; index < text.Length;)
        {
            var start = text.IndexOf(needle, index, StringComparison.Ordinal);

            if (start < 0)
            {
                yield break;
            }

            var openParen = start + needle.Length - 1;

            if (!TryExtractParenthesizedContent(text, openParen, out var arguments))
            {
                index = start + needle.Length;
                continue;
            }

            yield return arguments;
            index = openParen + arguments.Length + 2;
        }
    }

    private static bool HasSqlParameterArguments(string arguments)
    {
        var argumentCount = SplitTopLevelCommaSeparated(arguments)
            .Select(static part => part.Trim())
            .Count(static part => part.Length > 0);

        return argumentCount >= 2;
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

    private static bool TryExtractParenthesizedContent(string text, int openParenIndex, out string content)
    {
        content = string.Empty;

        if (!TryFindClosingParenthesis(text, openParenIndex, out var closeParenIndex))
        {
            return false;
        }

        content = text[(openParenIndex + 1)..closeParenIndex];
        return true;
    }

    private static bool TryFindClosingParenthesis(string text, int openParenIndex, out int closeParenIndex)
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

    private static bool HasUnorderedTakeWithoutRowCap(string normalized)
    {
        if (!normalized.Contains(".Take(", StringComparison.Ordinal)
            && !normalized.Contains(".TakeAsync(", StringComparison.Ordinal)
            && !normalized.Contains("Queryable.Take(", StringComparison.Ordinal))
        {
            return false;
        }

        if (normalized.Contains("OrderBy", StringComparison.Ordinal))
        {
            return false;
        }

        return !IsSingleRowCapTake(normalized);
    }

    private static bool IsSingleRowCapTake(string normalized)
    {
        return normalized.Contains(".Take(1)", StringComparison.Ordinal)
               || normalized.Contains(".Take( 1)", StringComparison.Ordinal)
               || (normalized.Contains("Queryable.Take(", StringComparison.Ordinal)
                   && normalized.Contains(", 1)", StringComparison.Ordinal));
    }

    private static bool MaterializesWithoutTake(string normalized)
    {
        return (normalized.Contains(".ToList()", StringComparison.Ordinal)
                || normalized.Contains(".ToListAsync(", StringComparison.Ordinal)
                || normalized.Contains(".ToArray()", StringComparison.Ordinal)
                || normalized.Contains(".ToArrayAsync(", StringComparison.Ordinal))
               && !normalized.Contains(".Take(", StringComparison.Ordinal)
               && !normalized.Contains(".TakeAsync(", StringComparison.Ordinal);
    }

    private static bool IsIntentionalFullListMaterialization(string? containingMethodName)
    {
        return string.Equals(containingMethodName, "ListAllAsync", StringComparison.Ordinal);
    }

    private static bool UsesUnboundedTerminalFirst(string normalized)
    {
        if (normalized.Contains(".Take(", StringComparison.Ordinal)
            || normalized.Contains(".TakeAsync(", StringComparison.Ordinal))
        {
            return false;
        }

        if (!UsesTerminalFirst(normalized))
        {
            return false;
        }

        return !HasBoundedFirstFilter(normalized);
    }

    private static bool UsesTerminalFirst(string normalized)
    {
        return normalized.Contains(".First()", StringComparison.Ordinal)
               || normalized.Contains(".FirstOrDefault()", StringComparison.Ordinal)
               || normalized.Contains(".FirstAsync(", StringComparison.Ordinal)
               || normalized.Contains(".FirstOrDefaultAsync(", StringComparison.Ordinal);
    }

    private static bool HasBoundedFirstFilter(string normalized)
    {
        if (normalized.Contains(".Where(", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var method in new[]
                 {
                     "FirstOrDefaultAsync(",
                     "FirstAsync(",
                     "FirstOrDefault(",
                     "First("
                 })
        {
            var index = 0;

            while ((index = normalized.IndexOf(method, index, StringComparison.Ordinal)) >= 0)
            {
                var openParen = index + method.Length - 1;

                if (TryExtractParenthesizedContent(normalized, openParen, out var arguments)
                    && arguments.Contains("=>", StringComparison.Ordinal))
                {
                    return true;
                }

                index += method.Length;
            }
        }

        return false;
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