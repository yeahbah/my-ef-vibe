namespace MyEfVibe;

internal static class ScriptDirectiveSyntax
{
    internal static bool ContainsScriptDirectives(string snippet)
    {
        foreach (var line in InputLineUtilities.SplitLines(snippet))
        {
            if (IsScriptDirectiveLine(line))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsScriptDirectiveLine(string line)
    {
        var trimmed = line.TrimStart();

        if (trimmed.Length == 0)
        {
            return false;
        }

        return trimmed.StartsWith("#load", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("#r ", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("#r\"", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("#r \"", StringComparison.OrdinalIgnoreCase);
    }
}
