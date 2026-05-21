namespace MyEfVibe.Tests;

public sealed class ExecutedSqlWarningRulesTests
{
    [Fact]
    public void AddExecutedSqlWarnings_FirstWithoutLimit_AddsWarning()
    {
        var warnings = new List<string>();

        ExecutedSqlWarningRules.AddExecutedSqlWarnings(
            "db.Products.First()",
            ["SELECT p.* FROM \"Products\" AS p"],
            warnings);

        Assert.Single(warnings);
    }

    [Fact]
    public void AddExecutedSqlWarnings_FirstWithLimit_DoesNotAddWarning()
    {
        var warnings = new List<string>();

        ExecutedSqlWarningRules.AddExecutedSqlWarnings(
            "db.Products.First()",
            ["SELECT p.* FROM \"Products\" AS p LIMIT 1"],
            warnings);

        Assert.Empty(warnings);
    }

    [Fact]
    public void AnalyzeSnippet_FirstWithoutTake_ReportsRule()
    {
        var warnings = LinqQueryWarningRules.AnalyzeSnippet("db.Products.First()");

        Assert.Contains(warnings, static w => w.RuleId == "first-without-take");
    }
}
