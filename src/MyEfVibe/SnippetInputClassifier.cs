namespace MyEfVibe;

internal static class SnippetInputClassifier
{
    internal static bool ShouldContinueOnNextLine(string snippetSoFar)
    {
        var trimmed = snippetSoFar.Trim();

        if (string.IsNullOrEmpty(trimmed))
            return true;

        if (HasUnbalancedDelimiters(trimmed))
            return true;

        if (EndsWithContinuationToken(trimmed))
            return true;

        return false;
    }

    internal static bool IsSubmitOnlyLine(string line)
        => line.Trim() == ";";

    private static bool EndsWithContinuationToken(string line)
    {
        if (line.EndsWith("\\", StringComparison.Ordinal))
            return true;

        if (line.EndsWith("?.", StringComparison.Ordinal))
            return true;

        if (line.EndsWith(".", StringComparison.Ordinal) && !line.EndsWith("..", StringComparison.Ordinal))
        {
            // Avoid treating numeric literals like `3.` as member-access continuation.
            if (line.Length >= 2 && char.IsDigit(line[^2]))
                return false;

            return true;
        }

        foreach (var token in new[] { ",", "+", "-", "*", "/", "&&", "||", "=>", "=", "{", "(", "[" })
        {
            if (line.EndsWith(token, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasUnbalancedDelimiters(string text)
    {
        var parenthesesDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var inString = false;
        var escape = false;

        foreach (var current in text)
        {
            if (inString)
            {
                if (escape)
                {
                    escape = false;

                    continue;
                }

                if (current == '\\')
                {
                    escape = true;

                    continue;
                }

                if (current == '"')
                    inString = false;

                continue;
            }

            if (current == '"')
            {
                inString = true;

                continue;
            }

            switch (current)
            {
                case '(':
                    parenthesesDepth++;

                    break;

                case ')':
                    parenthesesDepth--;

                    break;

                case '[':
                    bracketDepth++;

                    break;

                case ']':
                    bracketDepth--;

                    break;

                case '{':
                    braceDepth++;

                    break;

                case '}':
                    braceDepth--;

                    break;
            }
        }

        return parenthesesDepth > 0 || bracketDepth > 0 || braceDepth > 0 || inString;
    }
}
