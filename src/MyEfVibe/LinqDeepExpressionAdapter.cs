namespace MyEfVibe;

internal static class LinqDeepExpressionAdapter
{
    internal static string? TryCreateProbeExpression(
        string statementOrExpression,
        string? representativeEntityTypeName = null,
        Type? dbContextType = null,
        string? queryEntityTypeName = null,
        IEnumerable<string>? contextInstanceIdentifiers = null)
    {
        var normalized = NormalizeStatement(statementOrExpression);

        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        normalized = ReplaceContextAliases(normalized, contextInstanceIdentifiers);

        var probe = SqlTranslationProbe.TryCreateProbeExpression(normalized);

        if (probe is null)
            return null;

        var stubContext = new ProbeStubContext(
            dbContextType,
            queryEntityTypeName ?? representativeEntityTypeName);

        probe = ProbeParameterStubber.Stub(
            ProbeScriptFormatter.ToScriptExpression(probe),
            stubContext);

        if (string.IsNullOrWhiteSpace(representativeEntityTypeName)
            || !OpenGenericProbeBinder.ContainsOpenGenericTypeParameter(probe))
            return probe;

        return OpenGenericProbeBinder.Bind(probe, representativeEntityTypeName);
    }

    private static string NormalizeStatement(string code)
    {
        var trimmed = code.Trim().TrimEnd(';').Trim();

        trimmed = StripControlFlowWrapper(trimmed);
        trimmed = StripAssignmentRhs(trimmed);

        if (trimmed.StartsWith("return ", StringComparison.Ordinal))
            trimmed = trimmed["return ".Length..].Trim();

        if (trimmed.StartsWith("await ", StringComparison.Ordinal))
            trimmed = trimmed["await ".Length..].Trim();

        trimmed = StripNullCoalescingSuffix(trimmed);

        return trimmed.Trim().TrimEnd(';').Trim();
    }

    private static string StripControlFlowWrapper(string trimmed)
    {
        foreach (var keyword in new[] { "if", "while", "switch" })
        {
            var prefix = $"{keyword} (";

            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var openParenIndex = trimmed.IndexOf('(');

            if (openParenIndex < 0)
                return trimmed;

            if (!SqlTranslationProbe.TryExtractParenthesizedContent(trimmed, openParenIndex, out var inner))
                return trimmed;

            return inner.Trim();
        }

        return trimmed;
    }

    private static string StripNullCoalescingSuffix(string trimmed)
    {
        var depth = 0;
        var inString = '\0';

        for (var index = 0; index < trimmed.Length - 1; index++)
        {
            var character = trimmed[index];

            if (inString != '\0')
            {
                if (character == inString && trimmed[index - 1] != '\\')
                    inString = '\0';

                continue;
            }

            if (character is '"' or '\'')
            {
                inString = character;

                continue;
            }

            switch (character)
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

                case '?' when depth == 0 && trimmed[index + 1] == '?':
                    return trimmed[..index].Trim();
            }
        }

        return trimmed;
    }

    private static string StripAssignmentRhs(string trimmed)
    {
        if (!trimmed.StartsWith("var ", StringComparison.Ordinal))
            return trimmed;

        var equalsIndex = ProbeScriptFormatter.FindVarDeclarationEqualsIndex(trimmed);

        if (equalsIndex < 0)
            return trimmed;

        var rhs = trimmed[(equalsIndex + 1)..].Trim();

        if (rhs.StartsWith("await ", StringComparison.Ordinal))
            rhs = rhs["await ".Length..].Trim();

        return rhs;
    }

    private static string ReplaceContextAliases(string code, IEnumerable<string>? contextInstanceIdentifiers)
    {
        try
        {
            return DbContextAliasSyntaxRewriter.Rewrite(code, contextInstanceIdentifiers);
        }
        catch (Exception)
        {
            return ReplaceContextAliasesFallback(code);
        }
    }

    private static string ReplaceContextAliasesFallback(string code) =>
        code
            .Replace("this.dbContext.", "db.", StringComparison.Ordinal)
            .Replace("this.DbContext.", "db.", StringComparison.Ordinal)
            .Replace("this._dbContext.", "db.", StringComparison.Ordinal)
            .Replace("this._context.", "db.", StringComparison.Ordinal)
            .Replace("this.context.", "db.", StringComparison.Ordinal)
            .Replace("dbContext.", "db.", StringComparison.Ordinal)
            .Replace("DbContext.", "db.", StringComparison.Ordinal)
            .Replace("_dbContext.", "db.", StringComparison.Ordinal)
            .Replace("_context.", "db.", StringComparison.Ordinal)
            .Replace("applicationDbContext.", "db.", StringComparison.Ordinal)
            .Replace("_applicationDbContext.", "db.", StringComparison.Ordinal);
}
