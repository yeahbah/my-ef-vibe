namespace MyEfVibe;

internal static class SnippetNormalizer
{
    /// <summary>
    /// Prepares REPL input for Roslyn scripting. Trailing <c>;</c> on a final expression is removed so the
    /// script returns a value; statement forms (e.g. <c>var</c> declarations) keep their terminator.
    /// </summary>
    internal static string ForEvaluation(string snippet)
    {
        var trimmed = snippet.Trim();

        if (string.IsNullOrEmpty(trimmed))
            return trimmed;

        var lines = InputLineUtilities.SplitLines(trimmed);
        var lastNonEmptyIndex = IndexOfLastNonEmptyLine(lines);

        if (lastNonEmptyIndex < 0)
            return trimmed;

        if (lines.Length == 1)
            return NormalizeFinalLine(lines[0].TrimEnd());

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

        return InputLineUtilities.JoinLines(normalized);
    }

    private static int IndexOfLastNonEmptyLine(string[] lines)
    {
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            if (!string.IsNullOrWhiteSpace(lines[index]))
                return index;
        }

        return -1;
    }

    private static string NormalizeFinalLine(string line)
    {
        if (!line.EndsWith(';'))
            return line;

        if (RequiresStatementTerminator(line))
            return line;

        return line[..^1].TrimEnd();
    }

    private static bool RequiresStatementTerminator(string line)
    {
        var text = line.TrimEnd(';').TrimEnd();

        if (string.IsNullOrEmpty(text))
            return true;

        if (line.Count(static character => character == ';') > 1)
            return true;

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
            "#",
        ];

        foreach (var prefix in statementPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        if (text.StartsWith("using(", StringComparison.Ordinal) || text.StartsWith("using (", StringComparison.Ordinal))
            return true;

        // `int id = 1`, `string? name = null`, etc.
        if (LooksLikeTypeDeclaration(text))
            return true;

        return false;
    }

    private static bool LooksLikeTypeDeclaration(string text)
    {
        var equalsIndex = text.IndexOf('=');

        if (equalsIndex <= 0)
            return false;

        var left = text[..equalsIndex].Trim();

        if (left.Length == 0)
            return false;

        if (left is "var" or "dynamic")
            return true;

        // Heuristic: type name followed by identifier (`int id`, `List<int> items`).
        return char.IsLetter(left[0]) && left.Contains(' ');
    }

}
