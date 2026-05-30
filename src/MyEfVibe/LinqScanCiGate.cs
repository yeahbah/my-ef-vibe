namespace MyEfVibe;

internal sealed record LinqScanCiSummary(
    int TotalFindings,
    int InfoCount,
    int WarningCount,
    int ErrorCount,
    int CriticalCount,
    LinqScanSeverity? FailOnThreshold,
    bool ShouldFail);

internal static class LinqScanCiGate
{
    internal static IReadOnlyList<LinqScanFinding> Filter(
        IReadOnlyList<LinqScanFinding> findings,
        LinqScanSeverity? minSeverity)
    {
        return minSeverity is not { } threshold
            ? findings
            : findings
                .Where(finding => finding.Severity >= threshold)
                .ToArray();
    }

    internal static LinqScanCiSummary Summarize(
        IReadOnlyList<LinqScanFinding> findings,
        LinqScanSeverity? failOnThreshold)
    {
        var infoCount = 0;
        var warningCount = 0;
        var errorCount = 0;
        var criticalCount = 0;

        foreach (var finding in findings)
        {
            switch (finding.Severity)
            {
                case LinqScanSeverity.Info:
                    infoCount++;
                    break;

                case LinqScanSeverity.Warning:
                    warningCount++;
                    break;

                case LinqScanSeverity.Error:
                    errorCount++;
                    break;

                case LinqScanSeverity.Critical:
                    criticalCount++;
                    break;
            }
        }

        var shouldFail = failOnThreshold is { } threshold
                         && findings.Any(finding => finding.Severity >= threshold);

        return new LinqScanCiSummary(
            findings.Count,
            infoCount,
            warningCount,
            errorCount,
            criticalCount,
            failOnThreshold,
            shouldFail);
    }

    internal static int GetExitCode(LinqScanCiSummary summary)
    {
        return summary.ShouldFail ? 1 : 0;
    }
}