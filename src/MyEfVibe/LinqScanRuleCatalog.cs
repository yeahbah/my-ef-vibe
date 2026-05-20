namespace MyEfVibe;

/// <summary>
/// Maps heuristic rule ids to severity. Severity is determined only by <see cref="GetSeverity(string)"/>,
/// not by SQL translation outcome or other runtime signals.
/// </summary>
internal static class LinqScanRuleCatalog
{
    internal static LinqScanSeverity GetSeverity(string ruleId) =>
        ruleId switch
        {
            "unbounded-materialize" => LinqScanSeverity.Critical,
            "n-plus-one" => LinqScanSeverity.Error,
            "raw-sql" => LinqScanSeverity.Warning,
            "raw-sql-unparameterized" => LinqScanSeverity.Error,
            "cartesian" => LinqScanSeverity.Warning,
            "client-eval" => LinqScanSeverity.Warning,
            "unordered-take" => LinqScanSeverity.Warning,
            "query-site" => LinqScanSeverity.Info,
            _ => LinqScanSeverity.Warning,
        };

    internal static bool TryParseSeverity(string? raw, out LinqScanSeverity severity)
    {
        severity = default;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return Enum.TryParse(raw.Trim(), ignoreCase: true, out severity);
    }

    internal static string ToDisplayString(LinqScanSeverity severity) =>
        severity switch
        {
            LinqScanSeverity.Info => "info",
            LinqScanSeverity.Warning => "warning",
            LinqScanSeverity.Error => "error",
            LinqScanSeverity.Critical => "critical",
            _ => severity.ToString().ToLowerInvariant(),
        };
}
