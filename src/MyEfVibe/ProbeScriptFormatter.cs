using System.Text;

namespace MyEfVibe;

/// <summary>
///     Formats deep-scan probe expressions for Roslyn script evaluation.
/// </summary>
internal static class ProbeScriptFormatter
{
    /// <summary>
    ///     Collapses multiline query chains to one line so scripting does not treat each line as a separate statement.
    /// </summary>
    internal static string ToScriptExpression(string probeExpression, bool preserveLeadingAwait = false)
    {
        if (string.IsNullOrWhiteSpace(probeExpression))
        {
            return probeExpression;
        }

        probeExpression = StripLeadingReturnAwait(
            StripVariableDeclarationPrefix(probeExpression),
            preserveLeadingAwait);

        probeExpression = CollapseWhitespace(probeExpression);

        return EfProbeExpressionSanitizer.RemoveTranslationNeutralOperators(probeExpression);
    }

    private static string CollapseWhitespace(string expression)
    {
        var builder = new StringBuilder(expression.Length);
        var previousWasSpace = false;

        foreach (var character in expression.ReplaceLineEndings(" "))
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasSpace = false;
        }

        return builder.ToString().Trim().TrimEnd(';').Trim();
    }

    private static string StripVariableDeclarationPrefix(string expression)
    {
        var trimmed = expression.Trim();

        if (!trimmed.StartsWith("var ", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var equalsIndex = FindVarDeclarationEqualsIndex(trimmed);

        if (equalsIndex < 0)
        {
            return trimmed;
        }

        return trimmed[(equalsIndex + 1)..].Trim();
    }

    /// <summary>
    ///     Locates the <c>=</c> in a leading <c>var name = …</c> declaration only (not <c>==</c> or <c>=</c> inside
    ///     lambdas/anonymous types).
    /// </summary>
    internal static int FindVarDeclarationEqualsIndex(string text)
    {
        if (!text.StartsWith("var ", StringComparison.Ordinal))
        {
            return -1;
        }

        var index = 4;

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        while (index < text.Length && IsIdentifierPart(text[index]))
        {
            index++;
        }

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index >= text.Length || text[index] != '=')
        {
            return -1;
        }

        if (index > 0 && text[index - 1] is '=' or '!' or '<' or '>')
        {
            return -1;
        }

        if (index + 1 < text.Length && text[index + 1] is '=' or '>')
        {
            return -1;
        }

        return index;
    }

    private static bool IsIdentifierPart(char character)
    {
        return char.IsLetterOrDigit(character) || character is '_' or '@';
    }

    private static int FindSimpleAssignmentEqualsIndex(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '=')
            {
                continue;
            }

            if (index > 0 && text[index - 1] is '=' or '!' or '<' or '>')
            {
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] is '=' or '>')
            {
                continue;
            }

            return index;
        }

        return -1;
    }

    private static string StripLeadingReturnAwait(string expression, bool preserveLeadingAwait = false)
    {
        var trimmed = expression.Trim();

        if (trimmed.StartsWith("return ", StringComparison.Ordinal))
        {
            trimmed = trimmed["return ".Length..].Trim();
        }

        if (!preserveLeadingAwait && trimmed.StartsWith("await ", StringComparison.Ordinal))
        {
            trimmed = trimmed["await ".Length..].Trim();
        }

        return trimmed;
    }
}