namespace MyEfVibe.Tests;

public sealed class QueryPagingRewriterTests
{
    [Fact]
    public void TryApplyPaging_adds_take_for_first_page()
    {
        var rewritten = QueryPagingRewriter.TryApplyPaging("db.Products.ToList()", skip: 0, pageSize: 100);

        Assert.Equal("db.Products.Take(100).ToList()", ProbeTestHelper.CollapseWhitespace(rewritten));
    }

    [Fact]
    public void TryApplyPaging_adds_skip_and_take_for_later_pages()
    {
        var rewritten = QueryPagingRewriter.TryApplyPaging(
            "db.Products.OrderBy(p => p.Name).ToList()",
            skip: 100,
            pageSize: 100);

        Assert.Equal(
            "db.Products.OrderBy(p => p.Name).Skip(100).Take(100).ToList()",
            ProbeTestHelper.CollapseWhitespace(rewritten));
    }

    [Fact]
    public void TryApplyPaging_replaces_existing_skip_take()
    {
        var rewritten = QueryPagingRewriter.TryApplyPaging(
            "db.Products.Take(25).Skip(50).ToList()",
            skip: 100,
            pageSize: 100);

        Assert.Equal("db.Products.Skip(100).Take(100).ToList()", ProbeTestHelper.CollapseWhitespace(rewritten));
    }

    [Fact]
    public void SupportsPaging_is_true_for_list_materializers()
    {
        Assert.True(QueryPagingRewriter.SupportsPaging("db.Products.ToList()"));
        Assert.False(QueryPagingRewriter.SupportsPaging("db.Products.Count()"));
    }
}
