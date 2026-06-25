namespace MyEfVibe.Tests;

public sealed class SqlToLinqConverterTests
{
    private sealed class FakeProduct
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class FakeDbContext
    {
        public IQueryable<FakeProduct> Products => Array.Empty<FakeProduct>().AsQueryable();
    }

    [Fact]
    public void Convert_maps_simple_select_with_where_and_top()
    {
        var draft = SqlToLinqConverter.Convert(
            new FakeDbContext(),
            "SELECT TOP 10 * FROM Products WHERE Name = 'Helmet'");

        Assert.Contains("db.Products", draft.Linq, StringComparison.Ordinal);
        Assert.Contains(".Where(x => x.Name == \"Helmet\")", draft.Linq, StringComparison.Ordinal);
        Assert.Contains(".Take(10)", draft.Linq, StringComparison.Ordinal);
        Assert.Equal("high", draft.Confidence);
    }

    [Fact]
    public void Convert_reports_missing_from_clause()
    {
        var draft = SqlToLinqConverter.Convert(new FakeDbContext(), "SELECT 1");

        Assert.Contains("FROM clause", draft.Unsupported[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("low", draft.Confidence);
    }
}

public sealed class SqlSimilarityTests
{
    [Fact]
    public void Compare_returns_higher_score_for_similar_sql()
    {
        var score = SqlSimilarity.Compare(
            "SELECT Id, Name FROM Products WHERE Name = 'x'",
            "SELECT [p].[Id], [p].[Name] FROM [Products] AS [p] WHERE [p].[Name] = @p0");

        Assert.True(score > 0.2);
    }
}

public sealed class ServeProtocolTests_SqlToLinq
{
    [Fact]
    public void TryParseRequest_parses_sqlToLinq()
    {
        var request = ServeProtocol.TryParseRequest("""{"type":"sqlToLinq","sql":"SELECT * FROM Products"}""");

        Assert.NotNull(request);
        Assert.Equal("sqlToLinq", request!.Type);
        Assert.Equal("SELECT * FROM Products", request.Sql);
    }
}
