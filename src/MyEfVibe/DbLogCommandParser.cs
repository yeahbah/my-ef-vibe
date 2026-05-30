namespace MyEfVibe;

internal static class DbLogCommandParser
{
    internal static bool TryApplyOnArguments(string[] tokens, DbLogSettings settings, out string? error)
    {
        error = null;
        var sawLevel = false;
        var sawVerbose = false;

        foreach (var token in tokens)
        {
            if (string.Equals(token, "verbose", StringComparison.OrdinalIgnoreCase))
            {
                settings.Verbose = true;
                sawVerbose = true;
                continue;
            }

            if (DbLogLevelParser.TryParse(token, out var level))
            {
                settings.Level = level;
                sawLevel = true;
                continue;
            }

            error =
                $"Unknown option '{token}'. Use trace, debug, information, warning, error, critical, none, and/or verbose.";
            return false;
        }

        _ = sawLevel;
        _ = sawVerbose;
        return true;
    }

    internal static string FormatMode(DbLogSettings settings)
    {
        return settings.Verbose ? "verbose" : "sql-only";
    }

    internal static string FormatStatus(DbLogSettings settings)
    {
        return settings.Enabled
            ? $"on ({DbLogLevelParser.Format(settings.Level)} · {FormatMode(settings)})"
            : "off";
    }
}