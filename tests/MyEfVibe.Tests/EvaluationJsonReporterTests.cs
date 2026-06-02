namespace MyEfVibe.Tests;

public sealed class EvaluationJsonReporterTests
{
    [Fact]
    public void BuildSuccess_IncludesExecutedSqlAndMetrics()
    {
        var metrics = new EvaluationMetrics
        {
            Snippet = "db.Products.Count()",
            TotalMilliseconds = 12,
            DatabaseMilliseconds = 5,
            SqlCommandCount = 1,
            TranslatedSql = null,
            ExecutedSql = ["SELECT COUNT(*) FROM Products"],
            ResultKind = ResultKind.Scalar,
            ResultTypeName = "Int32",
            RowCount = 1,
            IsMaterialized = true,
            EstimatedBytes = 8,
            Warnings = Array.Empty<string>(),
            Succeeded = true,
        };

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
        var metrics = new EvaluationMetrics
        {
            Snippet = "db.Products.Take(1)",
            TotalMilliseconds = 8,
            DatabaseMilliseconds = null,
            SqlCommandCount = 0,
            TranslatedSql = null,
            ExecutedSql = ["SELECT \"p\".\"Id\" FROM \"Products\" AS \"p\" LIMIT 1"],
            ResultKind = ResultKind.Queryable,
            ResultTypeName = "IQueryable<Product>",
            RowCount = null,
            IsMaterialized = false,
            EstimatedBytes = 8,
            Warnings = [],
            Succeeded = true,  
        };
        var payload = EvaluationJsonReporter.BuildSuccess(null, metrics);

        Assert.True(payload.Success);
        Assert.Single(payload.Sql);
        Assert.Contains("Products", payload.Sql[0], StringComparison.Ordinal);
    }
}