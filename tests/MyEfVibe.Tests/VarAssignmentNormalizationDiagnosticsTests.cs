namespace MyEfVibe.Tests;

public sealed class VarAssignmentNormalizationDiagnosticsTests
{
    [Theory]
    [InlineData("var x = db.Products.Count();")]
    [InlineData("var x = db.Products.Count();\n")]
    [InlineData("var x = db.Users.Count();")]
    public void ForEvaluation_preserves_var_assignment_prefix(string snippet)
    {
        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeRewriterDbContext));

        Assert.StartsWith("var x =", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Count(x", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ForEvaluation_multiline_comment_does_not_strip_var_assignment()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "// c\nvar x = db.Products.Count();",
            typeof(FakeRewriterDbContext));

        Assert.Contains("var x =", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Count(x", normalized, StringComparison.Ordinal);
    }
}
