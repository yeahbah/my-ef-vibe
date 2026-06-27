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

    /// <summary>
    ///     Builds a script expression that calls <c>ToQueryString()</c>, parenthesizing query comprehensions so
    ///     member access binds to the whole query rather than the <c>select</c> identifier.
    /// </summary>
    internal static string ToQueryStringProbe(string probeExpression)
    {
        if (string.IsNullOrWhiteSpace(probeExpression))
        {
            return probeExpression;
        }

        var probe = ToScriptExpression(probeExpression);

        if (probe.Contains("ToQueryString", StringComparison.Ordinal))
        {
            return probe.TrimEnd(';').Trim();
        }

        probe = QueryComprehensionSyntax.WrapForTrailingMemberAccess(probe);

        return $"{probe}.ToQueryString()";
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

        var declaredName = TryParseVarDeclarationName(trimmed);
        var rhs = trimmed[(equalsIndex + 1)..].Trim();

        return FixMistakenCountSelfReference(declaredName, rhs);
    }

    internal static string? TryParseVarDeclarationName(string text)
    {
        if (!text.TrimStart().StartsWith("var ", StringComparison.Ordinal))
        {
            return null;
        }

        var equalsIndex = FindVarDeclarationEqualsIndex(text);

        if (equalsIndex < 0)
        {
            return null;
        }

        var declaration = text[..equalsIndex].TrimEnd();
        var nameStart = 4;

        while (nameStart < declaration.Length && char.IsWhiteSpace(declaration[nameStart]))
        {
            nameStart++;
        }

        var nameEnd = nameStart;

        while (nameEnd < declaration.Length && IsIdentifierPart(declaration[nameEnd]))
        {
            nameEnd++;
        }

        var name = declaration[nameStart..nameEnd].Trim();

        return name.Length == 0 ? null : name;
    }

    internal static string FixMistakenCountSelfReference(string? declaredName, string expression)
    {
        if (string.IsNullOrWhiteSpace(declaredName))
        {
            return expression;
        }

        var fixedExpression = expression;

        foreach (var method in new[] { "Count", "CountAsync" })
        {
            var mistaken = $".{method}({declaredName})";

            if (fixedExpression.Contains(mistaken, StringComparison.Ordinal))
            {
                fixedExpression = fixedExpression.Replace(mistaken, $".{method}()", StringComparison.Ordinal);
            }
        }

        return fixedExpression;
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