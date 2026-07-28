namespace MyEfVibe.Tests;

public class IgnoreQueryFiltersEvalStripTests
{
    [Fact]
    public void ForEvaluation_preserves_IgnoreQueryFilters_in_Where_pipeline()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.IgnoreQueryFilters().Where(x => x.Id > 0).ToList()",
            typeof(FakeRewriterDbContext));

        Assert.Contains("IgnoreQueryFilters", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ForEvaluation_preserves_IgnoreQueryFilters_in_Take_pipeline()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.IgnoreQueryFilters().Take(10).ToList()",
            typeof(FakeRewriterDbContext));

        Assert.Contains("IgnoreQueryFilters", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ForEvaluation_preserves_AsSplitQuery_in_Include_pipeline()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.Include(x => x.Name).AsSplitQuery().Take(10).ToList()",
            typeof(FakeRewriterDbContext));

        Assert.Contains("AsSplitQuery", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryAdapter_preserves_IgnoreQueryFilters()
    {
        var normalized = RepositorySnippetAdapter.PrepareForEvaluation(
            "await db.Users.IgnoreQueryFilters().Where(x => x.Id > 0).ToListAsync()",
            typeof(FakeRewriterDbContext));

        Assert.Contains("IgnoreQueryFilters", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ToScriptExpression_preserves_IgnoreQueryFilters()
    {
        var script = ProbeScriptFormatter.ToScriptExpression(
            "db.Users.IgnoreQueryFilters().Where(x => x.Id > 0)");

        Assert.Contains("IgnoreQueryFilters", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ToScriptExpression_still_strips_AsNoTracking()
    {
        var script = ProbeScriptFormatter.ToScriptExpression(
            "db.Users.AsNoTracking().Where(x => x.Id > 0)");

        Assert.DoesNotContain("AsNoTracking", script, StringComparison.Ordinal);
        Assert.Contains(".Where(", script, StringComparison.Ordinal);
    }
}
