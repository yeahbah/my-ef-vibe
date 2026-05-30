namespace MyEfVibe.Tests;

public sealed class EvaluationJsonReporterTests
{
    [Fact]
    public void BuildSuccess_IncludesExecutedSqlAndMetrics()
    {
        var metrics = new EvaluationMetrics(
            "db.Products.Count()",
            12,
            5,
            1,
            null,
            new[] { "SELECT COUNT(*) FROM Products" },
            ResultKind.Scalar,
            "Int32",
            1,
            true,
            8,
            Array.Empty<string>(),
            true);

        var payload = EvaluationJsonReporter.BuildSuccess(42, metrics);

        Assert.True(payload.Success);
        Assert.Equal("42", payload.Value);
        Assert.Single(payload.Sql);
        Assert.Equal(12, payload.Metrics.TotalMs);
        Assert.Equal(1, payload.Metrics.RowCount);
    }

    [Fact]
    public void BuildFailure_IncludesErrorAndWarnings()
    {
        var metrics = EvaluationMetrics.Failed("db.Bad", 3, "Compilation error");

        var payload = EvaluationJsonReporter.BuildFailure(metrics, "Compilation error");

        Assert.False(payload.Success);
        Assert.Equal("Compilation error", payload.Error);
        Assert.Equal("db.Bad", payload.Snippet);
        Assert.Equal(3, payload.Metrics.TotalMs);
    }

    [Fact]
    public void BuildSuccess_UsesTranslatedSqlWhenNoExecutedSql()
    {
        var metrics = new EvaluationMetrics(
            "db.Products.Take(1)",
            8,
            null,
            0,
            "SELECT \"p\".\"Id\" FROM \"Products\" AS \"p\" LIMIT 1",
            Array.Empty<string>(),
            ResultKind.Queryable,
            "IQueryable<Product>",
            null,
            false,
            null,
            Array.Empty<string>(),
            true);

        var payload = EvaluationJsonReporter.BuildSuccess(null, metrics);

        Assert.True(payload.Success);
        Assert.Single(payload.Sql);
        Assert.Contains("Products", payload.Sql[0], StringComparison.Ordinal);
    }
}