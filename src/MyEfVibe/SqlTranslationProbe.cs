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

    private static readonly string[] TerminalMethodNames =
    [
        "ToListAsync",
        "ToArrayAsync",
        "CountAsync",
        "FirstAsync",
        "FirstOrDefaultAsync",
        "SingleAsync",
        "SingleOrDefaultAsync",
        "AnyAsync",
        "MaxAsync",
        "MinAsync",
        "AverageAsync",
        "SumAsync",
        "ToList",
        "ToArray",
        "Count",
        "First",
        "FirstOrDefault",
        "Single",
        "SingleOrDefault",
        "Any",
        "Max",
        "Min",
        "Average",
        "Sum",
        "AsEnumerable",
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

        return TryStripTrailingTerminalCall(trimmed);
    }

    internal static bool TryExtractParenthesizedContent(string text, int openParenIndex, out string content)
    {
        content = string.Empty;

        if (!TryFindClosingParenthesis(text, openParenIndex, out var closeParenIndex))
            return false;

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
                continue;

            var openParenIndex = index + needle.Length - 1;

            if (!TryFindClosingParenthesis(expression, openParenIndex, out var closeParenIndex))
                continue;

            if (!IsEndOfExpression(expression, closeParenIndex + 1))
                continue;

            var probe = expression[..index].TrimEnd();

            return string.IsNullOrWhiteSpace(probe) ? null : probe;
        }

        return null;
    }

    private static bool IsEndOfExpression(string expression, int startIndex)
    {
        for (var index = startIndex; index < expression.Length; index++)
        {
            var character = expression[index];

            if (character is ' ' or '\t' or '\r' or '\n')
                continue;

            return false;
        }

        return true;
    }

    private static bool TryFindClosingParenthesis(string text, int openParenIndex, out int closeParenIndex)
    {
        closeParenIndex = -1;

        if (openParenIndex < 0 || openParenIndex >= text.Length || text[openParenIndex] != '(')
            return false;

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
                return index;

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
                return index;

            index++;
        }

        return text.Length - 1;
    }
}
