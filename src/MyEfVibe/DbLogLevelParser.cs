namespace MyEfVibe;

internal static class DbLogLevelParser
{
    internal static bool TryParse(string raw, out DbLogLevel level)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "trace":
                level = DbLogLevel.Trace;
                return true;
            case "debug":
                level = DbLogLevel.Debug;
                return true;
            case "information":
            case "info":
                level = DbLogLevel.Information;
                return true;
            case "warning":
            case "warn":
                level = DbLogLevel.Warning;
                return true;
            case "error":
                level = DbLogLevel.Error;
                return true;
            case "critical":
                level = DbLogLevel.Critical;
                return true;
            case "none":
                level = DbLogLevel.None;
                return true;
            default:
                level = DbLogLevel.Information;
                return false;
        }
    }

    internal static string Format(DbLogLevel level)
    {
        return level switch
        {
            DbLogLevel.Information => "information",
            DbLogLevel.Warning => "warning",
            _ => level.ToString().ToLowerInvariant()
        };
    }
}