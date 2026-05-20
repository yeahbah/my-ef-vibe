namespace MyEfVibe.Tests;

public sealed class LinqQueryWarningRulesTests
{
    [Fact]
    public void AnalyzeSnippet_ExecuteSqlRawWithParameters_ReportsRawSqlWarning()
    {
        const string snippet =
            """await DbContext.Database.ExecuteSqlRawAsync("DELETE FROM T WHERE Id = {0}", id);""";

        var warnings = LinqQueryWarningRules.AnalyzeSnippet(snippet);

        Assert.Contains(warnings, static w => w.RuleId == "raw-sql");
        Assert.DoesNotContain(warnings, static w => w.RuleId == "raw-sql-unparameterized");
    }

    [Fact]
    public void AnalyzeSnippet_ExecuteSqlRawSingleArgument_ReportsUnparameterizedError()
    {
        const string snippet =
            "await DbContext.Database.ExecuteSqlRawAsync($\"DELETE FROM T WHERE Id = {id}\");";

        var warnings = LinqQueryWarningRules.AnalyzeSnippet(snippet);

        Assert.Contains(warnings, static w => w.RuleId == "raw-sql-unparameterized");
        Assert.DoesNotContain(warnings, static w => w.RuleId == "raw-sql");
    }

    [Fact]
    public void AnalyzeSnippet_FromSqlInterpolated_DoesNotReportRawSqlRules()
    {
        const string snippet =
            "return DbContext.Products.FromSqlInterpolated($\"SELECT * FROM Products WHERE Id = {id}\").ToList();";

        var warnings = LinqQueryWarningRules.AnalyzeSnippet(snippet);

        Assert.DoesNotContain(warnings, static w => w.RuleId.StartsWith("raw-sql", StringComparison.Ordinal));
    }
}
