namespace MyEfVibe.Tests;

public sealed class SqlTranslationProbeBoundedMaterializationTests
{
    [Fact]
    public void TryRewriteBoundedTerminalQuery_adds_take_before_to_list()
    {
        var rewritten = SqlTranslationProbe.TryRewriteBoundedTerminalQuery("db.Products.ToList()");

        Assert.NotNull(rewritten);
        Assert.Equal("db.Products.Take(100).ToList()", ProbeTestHelper.CollapseWhitespace(rewritten));
    }

    [Fact]
    public void TryRewriteBoundedTerminalQuery_adds_take_before_to_list_async()
    {
        var rewritten = SqlTranslationProbe.TryRewriteBoundedTerminalQuery(
            "db.Products.OrderBy(p => p.Name).ToListAsync()");

        Assert.NotNull(rewritten);
        Assert.Contains(".Take(100)", rewritten, StringComparison.Ordinal);
        Assert.EndsWith(".ToListAsync()", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRewriteBoundedTerminalQuery_respects_existing_take()
    {
        var rewritten = SqlTranslationProbe.TryRewriteBoundedTerminalQuery("db.Products.Take(25).ToList()");

        Assert.Null(rewritten);
    }

    [Fact]
    public void TryRewriteBoundedTerminalQuery_skips_include_chains()
    {
        var rewritten = SqlTranslationProbe.TryRewriteBoundedTerminalQuery(
            """
            db.Orders
                .Include(o => o.Customer)
                .ToList()
            """);

        Assert.Null(rewritten);
    }

    [Fact]
    public void TryRewriteBoundedTerminalQuery_honors_unbounded_attribute()
    {
        var rewritten = SqlTranslationProbe.TryRewriteBoundedTerminalQuery(
            """
            #[Unbounded]
            db.Products.ToList()
            """);

        Assert.Null(rewritten);
    }

    [Fact]
    public void DescribeAutoMaterializationLimit_reports_after_snippet_normalization()
    {
        const string original = "db.Products.ToList()";
        var normalized = SnippetNormalizer.ForEvaluation(original, typeof(FakeRewriterDbContext));

        var warning = SqlTranslationProbe.DescribeAutoMaterializationLimit(normalized, original);

        Assert.NotNull(warning);
        Assert.Contains("100 rows", warning, StringComparison.Ordinal);
    }
}
