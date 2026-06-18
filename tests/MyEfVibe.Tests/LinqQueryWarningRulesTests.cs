using MyEfVibe.Linq;

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
    public void AnalyzeSnippet_SingleIncludeWithThenInclude_DoesNotReportCartesian()
    {
        const string snippet = """
                               return await DbContext.Addresses
                                   .Include(a => a.StateProvince)
                                   .ThenInclude(b => b.CountryRegion)
                                   .FirstOrDefaultAsync();
                               """;

        var warnings = LinqQueryWarningRules.AnalyzeSnippet(snippet);

        Assert.DoesNotContain(warnings, static w => w.RuleId == "cartesian");
    }

    [Fact]
    public void AnalyzeSnippet_TwoTopLevelIncludes_ReportsCartesian()
    {
        const string snippet = """
                               return await DbContext.Employees
                                   .Include(e => e.EmployeeDepartmentHistory).ThenInclude(h => h.Department)
                                   .Include(e => e.EmployeeDepartmentHistory).ThenInclude(h => h.Shift)
                                   .FirstOrDefaultAsync();
                               """;

        var warnings = LinqQueryWarningRules.AnalyzeSnippet(snippet);

        Assert.Contains(warnings, static w => w.RuleId == "cartesian");
    }

    [Fact]
    public void AnalyzeSnippet_FirstOrDefaultAsyncWithPredicate_DoesNotReportFirstWithoutTake()
    {
        const string snippet = """
                               return await _dbContext.Set<Currency>()
                                   .AsNoTracking()
                                   .FirstOrDefaultAsync(x => x.CurrencyCode == code, cancellationToken);
                               """;

        var warnings = LinqQueryWarningRules.AnalyzeSnippet(snippet);

        Assert.DoesNotContain(warnings, static w => w.RuleId == "first-without-take");
    }

    [Fact]
    public void AnalyzeSnippet_WhereBeforeFirstOrDefault_DoesNotReportFirstWithoutTake()
    {
        const string snippet = """
                               return await DbContext.Addresses
                                   .Where(x => x.AddressId == addressId)
                                   .FirstOrDefaultAsync();
                               """;

        var warnings = LinqQueryWarningRules.AnalyzeSnippet(snippet);

        Assert.DoesNotContain(warnings, static w => w.RuleId == "first-without-take");
    }

    [Fact]
    public void AnalyzeSnippet_UnfilteredFirstOrDefault_ReportsFirstWithoutTake()
    {
        const string snippet = "return await DbContext.Products.FirstOrDefaultAsync();";

        var warnings = LinqQueryWarningRules.AnalyzeSnippet(snippet);

        Assert.Contains(warnings, static w => w.RuleId == "first-without-take");
    }

    [Fact]
    public void AnalyzeSnippet_ListAllAsync_DoesNotReportUnboundedMaterialize()
    {
        const string snippet = """
                               return await _dbContext.Set<Currency>()
                                   .AsNoTracking()
                                   .ToListAsync(cancellationToken);
                               """;

        var warnings = LinqQueryWarningRules.AnalyzeSnippet(snippet, "ListAllAsync");

        Assert.DoesNotContain(warnings, static w => w.RuleId == "unbounded-materialize");
    }

    [Fact]
    public void AnalyzeSnippet_OtherMethod_StillReportsUnboundedMaterialize()
    {
        const string snippet = """
                               return await _dbContext.Set<Currency>()
                                   .AsNoTracking()
                                   .ToListAsync(cancellationToken);
                               """;

        var warnings = LinqQueryWarningRules.AnalyzeSnippet(snippet, "GetPagedAsync");

        Assert.Contains(warnings, static w => w.RuleId == "unbounded-materialize");
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