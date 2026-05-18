namespace MyEfVibe;

/// <summary>
/// Normalizes REPL line breaks and endings across Windows (<c>\r\n</c>) and Unix (<c>\n</c>).
/// </summary>
internal static class InputLineUtilities
{
    internal static string NormalizeNewlines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    internal static string[] SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [string.Empty];

        return NormalizeNewlines(text).Split('\n', StringSplitOptions.None);
    }

    internal static string StripCarriageReturns(string line) =>
        line.TrimEnd('\r');

    internal static string TrimLineEnd(string line) =>
        StripCarriageReturns(line).TrimEnd();

    internal static string JoinLines(IEnumerable<string> lines) =>
        string.Join("\n", lines.Select(StripCarriageReturns)).Trim();
}
