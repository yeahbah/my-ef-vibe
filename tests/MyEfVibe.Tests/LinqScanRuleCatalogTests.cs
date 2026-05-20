namespace MyEfVibe.Tests;

public sealed class LinqScanRuleCatalogTests
{
    [Theory]
    [InlineData("unbounded-materialize", LinqScanSeverity.Critical)]
    [InlineData("n-plus-one", LinqScanSeverity.Error)]
    [InlineData("raw-sql", LinqScanSeverity.Warning)]
    [InlineData("raw-sql-unparameterized", LinqScanSeverity.Error)]
    [InlineData("cartesian", LinqScanSeverity.Warning)]
    [InlineData("client-eval", LinqScanSeverity.Warning)]
    [InlineData("unordered-take", LinqScanSeverity.Warning)]
    [InlineData("query-site", LinqScanSeverity.Info)]
    public void GetSeverity_KnownRules_ReturnsExpected(string ruleId, LinqScanSeverity expected) =>
        Assert.Equal(expected, LinqScanRuleCatalog.GetSeverity(ruleId));

    [Theory]
    [InlineData("critical", LinqScanSeverity.Critical)]
    [InlineData("error", LinqScanSeverity.Error)]
    [InlineData("WARNING", LinqScanSeverity.Warning)]
    [InlineData("info", LinqScanSeverity.Info)]
    public void TryParseSeverity_ValidValues_ReturnsTrue(string raw, LinqScanSeverity expected)
    {
        Assert.True(LinqScanRuleCatalog.TryParseSeverity(raw, out var parsed));
        Assert.Equal(expected, parsed);
    }
}
