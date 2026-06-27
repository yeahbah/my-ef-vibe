namespace MyEfVibe;

internal static class SnippetNormalizer
{
    /// <summary>
    ///     Prepares REPL input for Roslyn scripting. Trailing <c>;</c> on a final expression is removed so the
    ///     script returns a value; statement forms (e.g. <c>var</c> declarations) keep their terminator.
    /// </summary>
    internal static string ForEvaluation(
        string snippet,
        Type? dbContextType = null,
        bool preserveAsyncQueries = false)
    {
        var trimmed = snippet.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return trimmed;
        }

        if (TrySplitVariableAssignment(trimmed, out var assignmentPrefix, out var declaredName, out var assignmentRhs))
        {
            return RewriteVariableAssignment(
                assignmentPrefix,
                declaredName,
                assignmentRhs,
                dbContextType,
                preserveAsyncQueries);
        }

        if (dbContextType is not null && LooksLikeRepositorySnippet(trimmed))
        {
            return RepositorySnippetAdapter.PrepareForEvaluation(trimmed, dbContextType, preserveAsyncQueries);
        }

        if (QueryComprehensionSyntax.IsQueryComprehensionOnlySnippet(trimmed))
        {
            return ProbeScriptFormatter.ToScriptExpression(trimmed);
        }

        var lines = InputLineUtilities.SplitLines(trimmed);
        var lastNonEmptyIndex = IndexOfLastNonEmptyLine(lines);

        if (lastNonEmptyIndex < 0)
        {
            return trimmed;
        }

        if (lines.Length == 1)
        {
            return RewriteBoundedEfQuery(
                NormalizeFinalLine(ReplaceContextAliases(lines[0].TrimEnd())),
                dbContextType,
                preserveAsyncQueries);
        }

        var normalized = new string[lines.Length];

        for (var index = 0; index < lines.Length; index++)
        {
            var line = InputLineUtilities.TrimLineEnd(lines[index]);

            if (string.IsNullOrWhiteSpace(line))
            {
                normalized[index] = line;

                continue;
            }

            normalized[index] = index == lastNonEmptyIndex
                ? NormalizeFinalLine(line)
                : line;
        }

        return RewriteBoundedEfQuery(
            ReplaceContextAliases(InputLineUtilities.JoinLines(normalized)),
            dbContextType,
            preserveAsyncQueries);
    }

    private static string ReplaceContextAliases(string snippet)
    {
        try
        {
            return DbContextAliasSyntaxRewriter.Rewrite(snippet);
        }
        catch (Exception)
        {
            return snippet;
        }
    }

    private static string RewriteBoundedEfQuery(
        string snippet,
        Type? dbContextType,
        bool preserveAsyncQueries = false)
    {
        if (dbContextType is null || ScriptDirectiveSyntax.ContainsScriptDirectives(snippet))
        {
            return snippet;
        }

        var options = preserveAsyncQueries
            ? EfReplQueryRewriteOptions.Async
            : EfReplQueryRewriteOptions.Sync;

        var rewritten = EfReplQueryableRewriter.TryRewriteToEfStaticCalls(snippet, dbContextType, options)
                        ?? snippet;

        rewritten = EfReplQueryableRewriter.TryCastDbSetRoots(rewritten, dbContextType, options)
                    ?? rewritten;

        return EfReplQueryableRewriter.TryRewriteWhereTakePipeline(rewritten, dbContextType, options)
               ?? EfReplQueryableRewriter.TryRewriteBareWhere(rewritten, dbContextType)
               ?? rewritten;
    }

    private static int IndexOfLastNonEmptyLine(string[] lines)
    {
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            if (!string.IsNullOrWhiteSpace(lines[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeFinalLine(string line)
    {
        var trimmedEnd = line.TrimEnd();

        if (RequiresStatementTerminator(trimmedEnd))
        {
            return trimmedEnd.EndsWith(';') ? line : $"{trimmedEnd};";
        }

        if (!trimmedEnd.EndsWith(';'))
        {
            return line;
        }

        return trimmedEnd[..^1].TrimEnd();
    }

    private static bool RequiresStatementTerminator(string line)
    {
        var text = line.TrimEnd(';').TrimEnd();

        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (line.Count(static character => character == ';') > 1)
        {
            return true;
        }

        ReadOnlySpan<string> statementPrefixes =
        [
            "var ",
            "using ",
            "global using ",
            "return ",
            "if ",
            "else",
            "for ",
            "foreach ",
            "while ",
            "do ",
            "switch ",
            "lock ",
            "try",
            "catch",
            "finally",
            "throw ",
            "break",
            "continue",
            "goto ",
            "fixed ",
            "unsafe ",
            "checked",
            "unchecked",
            "namespace ",
            "class ",
            "record ",
            "interface ",
            "enum ",
            "struct ",
            "delegate ",
            "event ",
            "#"
        ];

        foreach (var prefix in statementPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (text.StartsWith("using(", StringComparison.Ordinal) || text.StartsWith("using (", StringComparison.Ordinal))
        {
            return true;
        }

        if (LooksLikeSideEffectInvocation(text))
        {
            return true;
        }

        // `int id = 1`, `string? name = null`, etc.
        if (LooksLikeTypeDeclaration(text))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeRepositorySnippet(string snippet)
    {
        if (ScriptDirectiveSyntax.ContainsScriptDirectives(snippet))
        {
            return false;
        }

        if (QueryComprehensionSyntax.LooksLikeQueryComprehension(snippet))
        {
            return false;
        }

        return snippet.Contains("await ", StringComparison.Ordinal)
               || snippet.Contains("DbContext", StringComparison.Ordinal)
               || snippet.Contains("dbContext", StringComparison.Ordinal)
               || snippet.Contains("Async(", StringComparison.Ordinal)
               || snippet.Contains("cancellationToken", StringComparison.OrdinalIgnoreCase)
               || SqlTranslationProbe.ContainsEagerLoad(snippet);
    }

    private static bool TrySplitVariableAssignment(
        string snippet,
        out string prefix,
        out string? declaredName,
        out string rhs)
    {
        prefix = string.Empty;
        declaredName = null;
        rhs = string.Empty;

        if (!snippet.TrimStart().StartsWith("var ", StringComparison.Ordinal))
        {
            return false;
        }

        var equalsIndex = ProbeScriptFormatter.FindVarDeclarationEqualsIndex(snippet);

        if (equalsIndex < 0)
        {
            return false;
        }

        declaredName = ProbeScriptFormatter.TryParseVarDeclarationName(snippet);
        prefix = $"{snippet[..equalsIndex].TrimEnd()} = ";
        rhs = snippet[(equalsIndex + 1)..].TrimStart();

        if (string.IsNullOrWhiteSpace(rhs)
            || rhs.Contains('\n')
            || ContainsEmbeddedStatementSeparator(rhs)
            || rhs.Contains("await ", StringComparison.Ordinal)
            || rhs.Contains('{', StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsEmbeddedStatementSeparator(string rhs)
    {
        var trimmed = rhs.Trim();
        var semicolonIndex = trimmed.IndexOf(';');

        if (semicolonIndex < 0)
        {
            return false;
        }

        return trimmed[(semicolonIndex + 1)..].Trim().Length > 0;
    }

    private static bool LooksLikeSideEffectInvocation(string text)
    {
        return text.Contains("Console.WriteLine(", StringComparison.Ordinal)
               || text.Contains("Console.Write(", StringComparison.Ordinal)
               || text.Contains("Debug.WriteLine(", StringComparison.Ordinal)
               || text.Contains("Debug.Write(", StringComparison.Ordinal);
    }

    private static string RewriteVariableAssignment(
        string prefix,
        string? declaredName,
        string rhs,
        Type? dbContextType,
        bool preserveAsyncQueries)
    {
        var fixedRhs = ProbeScriptFormatter.FixMistakenCountSelfReference(declaredName, rhs);
        var normalizedRhs = NormalizeFinalLine(ReplaceContextAliases(fixedRhs.TrimEnd().TrimEnd(';')));
        var rewrittenRhs = RewriteBoundedEfQuery(normalizedRhs, dbContextType, preserveAsyncQueries);
        var combined = $"{prefix}{rewrittenRhs}";

        return combined.TrimEnd().EndsWith(';') ? combined : $"{combined};";
    }

    private static bool LooksLikeTypeDeclaration(string text)
    {
        var equalsIndex = text.IndexOf('=');

        if (equalsIndex <= 0)
        {
            return false;
        }

        var left = text[..equalsIndex].Trim();

        if (left.Length == 0)
        {
            return false;
        }

        if (left is "var" or "dynamic")
        {
            return true;
        }

        // Heuristic: type name followed by identifier (`int id`, `List<int> items`).
        return char.IsLetter(left[0]) && left.Contains(' ');
    }
}