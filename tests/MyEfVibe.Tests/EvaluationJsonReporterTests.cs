using MyEfVibe.Reporters;

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
            Warnings = [],
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
    public void BuildSuccess_UsesCapturedConsoleOutputWhenResultIsNull()
    {
        var metrics = new EvaluationMetrics
        {
            Snippet = "Console.WriteLine(\"hello\");",
            TotalMilliseconds = 1,
            SqlCommandCount = 0,
            ExecutedSql = [],
            ResultKind = ResultKind.Null,
            ResultTypeName = "null",
            IsMaterialized = true,
            Warnings = [],
            Succeeded = true,
            ConsoleOutput = "hello",
        };

        var payload = EvaluationJsonReporter.BuildSuccess(null, metrics);

        Assert.Equal("hello", payload.ConsoleOutput);
        Assert.Null(payload.Value);
    }

    [Fact]
    public void BuildSuccess_SeparatesConsoleOutputFromReturnValue()
    {
        var metrics = new EvaluationMetrics
        {
            Snippet = "Console.WriteLine(\"hello\"); new[] { 1, 2 };",
            TotalMilliseconds = 2,
            SqlCommandCount = 0,
            ExecutedSql = [],
            ResultKind = ResultKind.Enumerable,
            ResultTypeName = "Int32[]",
            RowCount = 2,
            IsMaterialized = true,
            Warnings = [],
            Succeeded = true,
            ConsoleOutput = "hello",
        };

        var payload = EvaluationJsonReporter.BuildSuccess(new[] { 1, 2 }, metrics);

        Assert.Equal("hello", payload.ConsoleOutput);
        Assert.Equal("2 rows", payload.Value);
    }

    [Fact]
    public void BuildSuccess_formats_byte_array_as_one_scalar_row()
    {
        var metrics = new EvaluationMetrics
        {
            Snippet = "db.Files.Select(file => file.Content).First()",
            TotalMilliseconds = 4,
            SqlCommandCount = 1,
            ExecutedSql = [],
            ResultKind = ResultKind.Object,
            ResultTypeName = "Byte[]",
            RowCount = 1,
            IsMaterialized = true,
            Warnings = [],
            Succeeded = true,
        };

        var content = new byte[] { 0x00, 0xFF, 0x10 };
        var payload = EvaluationJsonReporter.BuildSuccess(content, metrics);

        Assert.Equal(Convert.ToBase64String(content), payload.Value);
        Assert.NotNull(payload.Rows);
        Assert.Single(payload.Rows!);
        Assert.Equal(Convert.ToBase64String(content), payload.Rows![0]["value"]);
    }

    [Fact]
    public void BuildSuccess_formats_projected_byte_arrays_as_base64_values()
    {
        var metrics = new EvaluationMetrics
        {
            Snippet = "db.Files.Select(file => file.Content).ToList()",
            TotalMilliseconds = 4,
            SqlCommandCount = 1,
            ExecutedSql = [],
            ResultKind = ResultKind.Enumerable,
            ResultTypeName = "List`1",
            RowCount = 2,
            IsMaterialized = true,
            Warnings = [],
            Succeeded = true,
        };

        var first = new byte[] { 0x01, 0x02 };
        var second = new byte[] { 0x03 };
        var payload = EvaluationJsonReporter.BuildSuccess(new[] { first, second }, metrics);

        Assert.Equal("2 rows", payload.Value);
        Assert.NotNull(payload.Rows);
        Assert.Equal(2, payload.Rows!.Count);
        Assert.Equal(Convert.ToBase64String(first), payload.Rows[0]["value"]);
        Assert.Equal(Convert.ToBase64String(second), payload.Rows[1]["value"]);
        Assert.False(payload.Rows[0].ContainsKey("Length"));
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

    [Fact]
    public void BuildSuccess_serializes_entity_rows_with_navigation_properties()
    {
        var product = new EntityWithNavigation
        {
            ProductId = 7,
            Name = "Sprocket",
            ProductInventory = new InventoryWithBackReference
            {
                ProductId = 7,
                Quantity = 12,
                Product = null!,
            },
        };
        product.ProductInventory.Product = product;

        var metrics = new EvaluationMetrics
        {
            Snippet = "db.Products.Take(1).ToList()",
            TotalMilliseconds = 4,
            SqlCommandCount = 1,
            ExecutedSql = ["SELECT ..."],
            ResultKind = ResultKind.Enumerable,
            ResultTypeName = "List<Product>",
            RowCount = 1,
            IsMaterialized = true,
            Warnings = [],
            Succeeded = true,
        };

        var payload = EvaluationJsonReporter.BuildSuccess(new[] { product }, metrics);

        Assert.True(payload.Success);
        Assert.NotNull(payload.Rows);
        Assert.Single(payload.Rows!);
        Assert.Equal("7", payload.Rows![0]["ProductId"]);
        Assert.Equal("Sprocket", payload.Rows![0]["Name"]);
        Assert.False(payload.Rows![0].ContainsKey("ProductInventory"));
    }

    private sealed class EntityWithNavigation
    {
        public int ProductId { get; set; }

        public string Name { get; set; } = string.Empty;

        public InventoryWithBackReference ProductInventory { get; set; } = null!;
    }

    private sealed class InventoryWithBackReference
    {
        public int ProductId { get; set; }

        public short Quantity { get; set; }

        public EntityWithNavigation Product { get; set; } = null!;
    }
}
