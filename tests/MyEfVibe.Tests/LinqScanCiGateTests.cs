namespace MyEfVibe.Tests;

public sealed class LinqScanCiGateTests
{
    private static LinqScanFinding Finding(LinqScanSeverity severity) =>
        new("a.cs", 1, "code", "test-rule", "message", severity);

    [Fact]
    public void Filter_MinSeverityWarning_ExcludesInfo()
    {
        var findings = new[]
        {
            Finding(LinqScanSeverity.Info),
            Finding(LinqScanSeverity.Warning),
            Finding(LinqScanSeverity.Error),
        };

        var filtered = LinqScanCiGate.Filter(findings, LinqScanSeverity.Warning);

        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, static f => f.Severity == LinqScanSeverity.Info);
    }

    [Fact]
    public void Summarize_FailOnWarning_WithError_FailsCi()
    {
        var findings = new[] { Finding(LinqScanSeverity.Error) };

        var summary = LinqScanCiGate.Summarize(findings, LinqScanSeverity.Warning);

        Assert.True(summary.ShouldFail);
        Assert.Equal(1, LinqScanCiGate.GetExitCode(summary));
    }

    [Fact]
    public void Summarize_FailOnError_WithOnlyWarning_PassesCi()
    {
        var findings = new[] { Finding(LinqScanSeverity.Warning) };

        var summary = LinqScanCiGate.Summarize(findings, LinqScanSeverity.Error);

        Assert.False(summary.ShouldFail);
        Assert.Equal(0, LinqScanCiGate.GetExitCode(summary));
    }

    [Fact]
    public void Summarize_FailOnCritical_WithUnboundedMaterializeRule_FailsCi()
    {
        var findings = new[]
        {
            LinqScanFinding.Create("a.cs", 1, "code", "unbounded-materialize", "no take"),
        };

        var summary = LinqScanCiGate.Summarize(findings, LinqScanSeverity.Critical);

        Assert.True(summary.ShouldFail);
        Assert.Equal(LinqScanSeverity.Critical, findings[0].Severity);
    }
}
