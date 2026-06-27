namespace MyEfVibe.Tests;

public sealed class VarAssignmentCountTests
{
    [Fact]
    public void ForEvaluation_preserves_var_assignment_with_count()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "var x = db.Products.Count();",
            typeof(FakeRewriterDbContext));

        Assert.StartsWith("var x =", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Count(", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void ForEvaluation_rewrites_count_inside_var_assignment()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "var total = db.Users.Count();",
            typeof(FakeRewriterDbContext));

        Assert.StartsWith("var total =", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Count(db.Users", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Count(total", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ForEvaluation_fixes_self_referential_count_argument()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "var x = db.Users.Count(x);",
            typeof(FakeRewriterDbContext));

        Assert.StartsWith("var x =", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Count(db.Users", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Count(x", normalized, StringComparison.Ordinal);
    }
}
