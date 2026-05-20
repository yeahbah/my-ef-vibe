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
    public void TryCreateProbeExpression_BareQueryableWithAsQueryable_ReturnsChain()
    {
        const string expression = "db.Employees.Where(e => e.Active).AsQueryable()";

        var probe = SqlTranslationProbe.TryCreateProbeExpression(expression);

        Assert.NotNull(probe);
        Assert.Equal("db.Employees.Where(e => e.Active)", probe);
    }
}
