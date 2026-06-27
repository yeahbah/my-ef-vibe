namespace MyEfVibe;

internal static class ScriptDirectiveSplitter
{
    internal static (string Directives, string Body) SplitLeadingDirectives(string snippet)
    {
        var lines = InputLineUtilities.SplitLines(snippet);
        var directiveLines = new List<string>();
        var bodyStart = 0;

        for (; bodyStart < lines.Length; bodyStart++)
        {
            var line = lines[bodyStart];

            if (string.IsNullOrWhiteSpace(line))
            {
                if (directiveLines.Count > 0)
                {
                    directiveLines.Add(line);
                }

                continue;
            }

            if (ScriptDirectiveSyntax.IsScriptDirectiveLine(line))
            {
                directiveLines.Add(line);
                continue;
            }

            break;
        }

        var bodyLines = lines.AsSpan(bodyStart).ToArray();

        return (
            directiveLines.Count == 0 ? string.Empty : InputLineUtilities.JoinLines(directiveLines),
            bodyLines.Length == 0 ? string.Empty : InputLineUtilities.JoinLines(bodyLines));
    }
}
