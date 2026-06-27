namespace MyEfVibe;

internal static class QueryComprehensionSyntax
{
    internal static bool LooksLikeQueryComprehension(string snippet)
    {
        foreach (var line in InputLineUtilities.SplitLines(snippet))
        {
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (IsQueryComprehensionLine(trimmed))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsQueryComprehensionOnlySnippet(string snippet)
    {
        if (!LooksLikeQueryComprehension(snippet))
        {
            return false;
        }

        foreach (var line in InputLineUtilities.SplitLines(snippet))
        {
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (!IsQueryComprehensionLine(trimmed) && !IsQueryComprehensionTerminalSuffixLine(trimmed))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsFullyParenthesized(string expression)
    {
        var trimmed = expression.Trim();

        if (!trimmed.StartsWith('('))
        {
            return false;
        }

        return SqlTranslationProbe.TryFindClosingParenthesis(trimmed, 0, out var closeIndex)
               && SqlTranslationProbe.IsEndOfExpression(trimmed, closeIndex + 1);
    }

    internal static string WrapForTrailingMemberAccess(string expression)
    {
        var trimmed = expression.Trim().TrimEnd(';').Trim();

        if (LooksLikeQueryComprehension(trimmed) && !IsFullyParenthesized(trimmed))
        {
            return $"({trimmed})";
        }

        return trimmed;
    }

    private static bool IsQueryComprehensionTerminalSuffixLine(string line)
    {
        var text = line.TrimStart();

        while (text.StartsWith(')'))
        {
            text = text[1..].TrimStart();
        }

        return text.StartsWith('.') && text.Contains('(', StringComparison.Ordinal);
    }

    private static bool IsQueryComprehensionLine(string line)
    {
        var text = line.TrimStart();

        if (text.StartsWith('('))
        {
            text = text[1..].TrimStart();
        }

        ReadOnlySpan<string> keywords =
        [
            "from ",
            "where ",
            "select ",
            "join ",
            "into ",
            "let ",
            "orderby ",
            "group ",
            "on ",
            "equals ",
            "by "
        ];

        foreach (var keyword in keywords)
        {
            if (text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
