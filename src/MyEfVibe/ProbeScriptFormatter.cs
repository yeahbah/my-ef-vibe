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

    internal static bool TryStripToQueryStringTerminal(string expression, out string queryExpression)
    {
        queryExpression = string.Empty;

        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var probe = ToScriptExpression(expression).TrimEnd(';').Trim();

        if (!probe.EndsWith(".ToQueryString()", StringComparison.Ordinal))
        {
            return false;
        }

        queryExpression = probe[..^".ToQueryString()".Length].TrimEnd();
        queryExpression = QueryComprehensionSyntax.WrapForTrailingMemberAccess(queryExpression);

        return !string.IsNullOrWhiteSpace(queryExpression);
    }

    private static string CollapseWhitespace(string expression)
    {
        var builder = new StringBuilder(expression.Length);
        var previousWasSpace = false;
        var index = 0;

        while (index < expression.Length)
        {
            if (TryAppendPreservedLiteral(expression, ref index, builder))
            {
                previousWasSpace = false;
                continue;
            }

            var character = expression[index];

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                index++;
                continue;
            }

            builder.Append(character);
            previousWasSpace = false;
            index++;
        }

        return builder.ToString().Trim().TrimEnd(';').Trim();
    }

    /// <summary>
    ///     Copies a string or character literal verbatim (including interior whitespace) so probe formatting
    ///     does not rewrite filter values like <c>"John  Smith"</c> into <c>"John Smith"</c>.
    /// </summary>
    private static bool TryAppendPreservedLiteral(string expression, ref int index, StringBuilder builder)
    {
        if (index >= expression.Length)
        {
            return false;
        }

        if (expression[index] == '\'')
        {
            return TryAppendCharacterLiteral(expression, ref index, builder);
        }

        if (TryReadRawStringPrefix(expression, index, out var rawPrefixLength, out var quoteCount, out _))
        {
            return TryAppendRawStringLiteral(expression, ref index, builder, rawPrefixLength, quoteCount);
        }

        if (TryReadQuotedStringPrefix(expression, index, out var quotedPrefixLength, out var isVerbatim, out var isInterpolated))
        {
            return TryAppendQuotedStringLiteral(
                expression,
                ref index,
                builder,
                quotedPrefixLength,
                isVerbatim,
                isInterpolated);
        }

        return false;
    }

    private static bool TryAppendCharacterLiteral(string expression, ref int index, StringBuilder builder)
    {
        var start = index;
        index++; // opening '

        while (index < expression.Length)
        {
            var character = expression[index];

            if (character == '\\' && index + 1 < expression.Length)
            {
                index += 2;
                continue;
            }

            index++;

            if (character == '\'')
            {
                builder.Append(expression, start, index - start);
                return true;
            }
        }

        // Unterminated: preserve the remainder rather than corrupting it.
        builder.Append(expression, start, expression.Length - start);
        index = expression.Length;
        return true;
    }

    private static bool TryReadQuotedStringPrefix(
        string expression,
        int index,
        out int prefixLength,
        out bool isVerbatim,
        out bool isInterpolated)
    {
        prefixLength = 0;
        isVerbatim = false;
        isInterpolated = false;

        if (index >= expression.Length)
        {
            return false;
        }

        if (expression[index] == '"')
        {
            prefixLength = 1;
            return true;
        }

        if (expression[index] == '$' && index + 1 < expression.Length)
        {
            if (expression[index + 1] == '"')
            {
                prefixLength = 2;
                isInterpolated = true;
                return true;
            }

            if (expression[index + 1] == '@' && index + 2 < expression.Length && expression[index + 2] == '"')
            {
                prefixLength = 3;
                isVerbatim = true;
                isInterpolated = true;
                return true;
            }
        }

        if (expression[index] == '@' && index + 1 < expression.Length)
        {
            if (expression[index + 1] == '"')
            {
                prefixLength = 2;
                isVerbatim = true;
                return true;
            }

            if (expression[index + 1] == '$' && index + 2 < expression.Length && expression[index + 2] == '"')
            {
                prefixLength = 3;
                isVerbatim = true;
                isInterpolated = true;
                return true;
            }
        }

        return false;
    }

    private static bool TryAppendQuotedStringLiteral(
        string expression,
        ref int index,
        StringBuilder builder,
        int prefixLength,
        bool isVerbatim,
        bool isInterpolated)
    {
        var start = index;
        index += prefixLength;

        while (index < expression.Length)
        {
            var character = expression[index];

            if (isInterpolated && character == '{')
            {
                if (index + 1 < expression.Length && expression[index + 1] == '{')
                {
                    index += 2;
                    continue;
                }

                // Copy literal text before the hole, then collapse whitespace inside the hole (code).
                builder.Append(expression, start, index - start);
                builder.Append('{');
                index++;
                AppendCollapsedInterpolationHole(expression, ref index, builder);
                start = index;
                continue;
            }

            if (isVerbatim)
            {
                if (character == '"' && index + 1 < expression.Length && expression[index + 1] == '"')
                {
                    index += 2;
                    continue;
                }

                index++;

                if (character == '"')
                {
                    builder.Append(expression, start, index - start);
                    return true;
                }

                continue;
            }

            if (character == '\\' && index + 1 < expression.Length)
            {
                index += 2;
                continue;
            }

            index++;

            if (character == '"')
            {
                builder.Append(expression, start, index - start);
                return true;
            }
        }

        builder.Append(expression, start, expression.Length - start);
        index = expression.Length;
        return true;
    }

    private static void AppendCollapsedInterpolationHole(string expression, ref int index, StringBuilder builder)
    {
        var depth = 1;
        var previousWasSpace = false;

        while (index < expression.Length && depth > 0)
        {
            if (TryAppendPreservedLiteral(expression, ref index, builder))
            {
                previousWasSpace = false;
                continue;
            }

            var character = expression[index];

            if (character == '{')
            {
                depth++;
                builder.Append(character);
                previousWasSpace = false;
                index++;
                continue;
            }

            if (character == '}')
            {
                depth--;
                builder.Append(character);
                previousWasSpace = false;
                index++;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                index++;
                continue;
            }

            builder.Append(character);
            previousWasSpace = false;
            index++;
        }
    }

    private static bool TryReadRawStringPrefix(
        string expression,
        int index,
        out int prefixLength,
        out int quoteCount,
        out bool isInterpolated)
    {
        prefixLength = 0;
        quoteCount = 0;
        isInterpolated = false;

        var cursor = index;

        if (cursor < expression.Length && expression[cursor] == '$')
        {
            isInterpolated = true;
            cursor++;
        }

        if (cursor >= expression.Length || expression[cursor] != '"')
        {
            return false;
        }

        while (cursor < expression.Length && expression[cursor] == '"')
        {
            quoteCount++;
            cursor++;
        }

        if (quoteCount < 3)
        {
            return false;
        }

        prefixLength = cursor - index;
        return true;
    }

    private static bool TryAppendRawStringLiteral(
        string expression,
        ref int index,
        StringBuilder builder,
        int prefixLength,
        int quoteCount)
    {
        var start = index;
        var isInterpolated = expression[index] == '$';
        index += prefixLength;

        while (index < expression.Length)
        {
            if (isInterpolated && expression[index] == '{')
            {
                if (index + 1 < expression.Length && expression[index + 1] == '{')
                {
                    index += 2;
                    continue;
                }

                builder.Append(expression, start, index - start);
                builder.Append('{');
                index++;
                AppendCollapsedInterpolationHole(expression, ref index, builder);
                start = index;
                continue;
            }

            if (expression[index] == '"')
            {
                var run = 0;

                while (index + run < expression.Length && expression[index + run] == '"')
                {
                    run++;
                }

                if (run == quoteCount)
                {
                    index += run;
                    builder.Append(expression, start, index - start);
                    return true;
                }

                index += run;
                continue;
            }

            index++;
        }

        builder.Append(expression, start, expression.Length - start);
        index = expression.Length;
        return true;
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