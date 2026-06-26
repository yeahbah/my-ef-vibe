using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class QueryComprehensionSnippetTests
{
    [Fact]
    public void ForEvaluation_multiline_query_comprehension_does_not_use_repository_adapter()
    {
        const string snippet = """
                               from reviews in db.Products
                               select reviews
                               """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.Contains("from reviews in db.Products", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("select reviews", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("global::MyEfVibe.ReplQueryableRuntime", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void ForEvaluation_single_line_query_comprehension_parses()
    {
        const string snippet = "from reviews in db.Products select reviews";

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.Equal(snippet, normalized);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void ForEvaluation_query_comprehension_with_where_parses()
    {
        const string snippet = """
                               from product in db.Products
                               where product.ListPrice > 0
                               select product
                               """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.Contains("where product.ListPrice > 0", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void TryCreateProbeExpression_query_comprehension_returns_bare_queryable()
    {
        const string snippet = """
                               from reviews in db.Products
                               select reviews
                               """;

        var probe = SqlTranslationProbe.TryCreateProbeExpression(snippet);

        Assert.NotNull(probe);
        Assert.Contains("from reviews in db.Products", probe, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("select reviews", probe, StringComparison.OrdinalIgnoreCase);
    }
}
