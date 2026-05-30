namespace MyEfVibe.Tests;

public sealed class SqlTranslationProbeTests
{
    [Fact]
    public void TryCreateProbeExpression_FirstOrDefaultAsyncWithPredicate_AddsWhereAndTake()
    {
        const string expression =
            "db.Departments.FirstOrDefaultAsync(s => s.DepartmentId == id, cancellationToken)";

        var probe = SqlTranslationProbe.TryCreateProbeExpression(expression);

        Assert.NotNull(probe);
        Assert.Contains(".Where(s => s.DepartmentId == id)", probe, StringComparison.Ordinal);
        Assert.EndsWith(".Take(1)", probe.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreateProbeExpression_FirstOrDefaultAsyncWithOnlyCancellationToken_AddsTake()
    {
        const string expression = "db.Departments.FirstOrDefaultAsync(cancellationToken)";

        var probe = SqlTranslationProbe.TryCreateProbeExpression(expression);

        Assert.NotNull(probe);
        Assert.Equal("db.Departments.Take(1)", ProbeTestHelper.CollapseWhitespace(probe));
    }

    [Fact]
    public void TryCreateProbeExpression_First_AddsTake()
    {
        const string expression = "db.Products.First()";

        var probe = SqlTranslationProbe.TryCreateProbeExpression(expression);

        Assert.NotNull(probe);
        Assert.Equal("db.Products.Take(1)", ProbeTestHelper.CollapseWhitespace(probe));
    }

    [Fact]
    public void TryCreateProbeExpression_IncludeChain_DoesNotAppendTakeForFirstOrDefaultAsync()
    {
        const string expression = """
                                  db.Employees
                                      .Include(e => e.PersonBusinessEntity)
                                      .Include(e => e.EmployeeDepartmentHistory)
                                          .ThenInclude(dh => dh.Department)
                                      .Where(e => e.BusinessEntityId == businessEntityId)
                                      .FirstOrDefaultAsync(cancellationToken)
                                  """;

        var probe = SqlTranslationProbe.TryCreateProbeExpression(expression);

        Assert.NotNull(probe);
        Assert.Contains(".Include(", probe, StringComparison.Ordinal);
        Assert.Contains(".ThenInclude(", probe, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(1)", probe, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreateProbeExpression_ThenIncludeOnly_still_skips_take_for_first_or_default()
    {
        const string expression = """
                                  db.Orders
                                      .Include(o => o.Lines)
                                      .ThenInclude(l => l.Product)
                                      .FirstOrDefaultAsync(cancellationToken)
                                  """;

        var probe = SqlTranslationProbe.TryCreateProbeExpression(expression);

        Assert.NotNull(probe);
        Assert.Contains(".ThenInclude(", probe, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(1)", probe, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreateProbeExpression_plain_query_still_appends_take_for_first_or_default_async()
    {
        const string expression =
            "db.Departments.Where(d => d.DepartmentId == 1).FirstOrDefaultAsync(cancellationToken)";

        var probe = SqlTranslationProbe.TryCreateProbeExpression(expression);

        Assert.NotNull(probe);
        Assert.DoesNotContain(".Include(", probe, StringComparison.Ordinal);
        Assert.EndsWith(".Take(1)", probe.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreateProbeExpression_BareQueryableWithAsQueryable_ReturnsChain()
    {
        const string expression = "db.Employees.Where(e => e.Active).AsQueryable()";

        var probe = SqlTranslationProbe.TryCreateProbeExpression(expression);

        Assert.NotNull(probe);
        Assert.Equal("db.Employees.Where(e => e.Active)", probe);
    }
}